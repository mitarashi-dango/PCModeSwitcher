using System.Text.Json;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class ModeEngine : IDisposable
{
    private readonly IReadOnlyList<IModeActionHandler> _handlers;
    private readonly SessionStore _sessionStore;
    private readonly AppLogger _logger;
    private readonly ProcessMonitorService _monitor;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ModeSessionSnapshot? _activeSession;
    private PcMode? _activeMode;
    private bool _disposed;

    public event EventHandler? SessionChanged;
    public bool HasActiveSession => _activeSession?.IsAwaitingRestore == true;
    public string? ActiveModeId => _activeSession?.ModeId;
    public string? ActiveModeName => _activeSession?.ModeName;

    public ModeEngine(
        IEnumerable<IModeActionHandler>? handlers = null,
        SessionStore? sessionStore = null,
        AppLogger? logger = null,
        ProcessMonitorService? monitor = null)
    {
        _handlers = handlers?.ToList() ??
        [
            new PowerPlanActionHandler(),
            new WindowsPowerModeActionHandler(),
            new PowerTimeoutActionHandler(),
            new PowerRequestActionHandler(),
            new DisplayModeActionHandler(),
            new AudioActionHandler(),
            new ProcessActionHandler(),
            new WindowPlacementActionHandler()
        ];
        _sessionStore = sessionStore ?? new SessionStore();
        _logger = logger ?? new AppLogger();
        _monitor = monitor ?? new ProcessMonitorService();
    }

    public async Task<OperationResult<ModeSessionSnapshot?>> GetIncompleteSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _sessionStore.LoadAsync(cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            _activeSession = result.Value;
            OnSessionChanged();
        }
        return result;
    }

    public async Task<ModeApplyResult> ApplyAsync(PcMode mode, CancellationToken cancellationToken = default)
    {
        if (!_operationGate.Wait(0))
            return SingleFailure("排他制御", "別のモード操作を実行中です。");
        try
        {
            if (_activeSession?.IsAwaitingRestore == true)
            {
                var restore = await RestoreCoreAsync(_activeSession, _activeMode, CancellationToken.None);
                if (!restore.IsSuccess)
                    return SingleFailure("元に戻す", "現在のモードを元に戻せなかったため、次のモードを適用しませんでした。", restore.Steps.FirstOrDefault(step => !step.IsSuccess)?.TechnicalDetails);
            }
            else
            {
                var persisted = await _sessionStore.LoadAsync(cancellationToken);
                if (!persisted.IsSuccess)
                    return SingleFailure("事前確認", persisted.UserMessage, persisted.TechnicalDetails);
                if (persisted.Value?.IsAwaitingRestore == true || persisted.Value?.IsApplying == true)
                    return SingleFailure("事前確認", "前回のモード設定を元に戻すか、記録を無視してから適用してください。");
            }

            if (!mode.IsEnabled)
                return SingleFailure("事前確認", "このモードは無効です。");

            var probe = await _sessionStore.ProbeWriteAsync(cancellationToken);
            if (!probe.IsSuccess)
                return SingleFailure("事前確認", probe.UserMessage, probe.TechnicalDetails);

            var session = new ModeSessionSnapshot
            {
                ModeId = mode.Id,
                ModeName = mode.Name,
                IsApplying = true
            };
            var context = new ModeActionContext { Mode = mode, Session = session };
            var preflight = new Dictionary<string, ActionPreflightResult>(StringComparer.Ordinal);
            foreach (var handler in _handlers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var check = await SafePreflightAsync(handler, context, cancellationToken);
                preflight[handler.Id] = check;
                if (!check.CanContinue)
                    return SingleFailure($"Preflight: {handler.DisplayName}", check.Message, check.TechnicalDetails);
            }

            var initialSave = await _sessionStore.SaveAsync(session, cancellationToken);
            if (!initialSave.IsSuccess)
                return SingleFailure("元に戻すための記録", initialSave.UserMessage, initialSave.TechnicalDetails);

            _activeSession = session;
            _activeMode = mode.Copy();
            OnSessionChanged();
            var output = new List<ApplyStepResult>();

            foreach (var handler in _handlers)
            {
                var check = preflight[handler.Id];
                if (check.Status != ActionExecutionStatus.Pending)
                {
                    var skipped = new ActionSnapshot
                    {
                        ActionId = handler.Id,
                        OriginalState = JsonSerializer.SerializeToElement(new { }),
                        StateCaptured = false,
                        ApplyResult = ActionResults.Create(handler, check.Status, check.Message, check.TechnicalDetails)
                    };
                    session.Actions.Add(skipped);
                    output.Add(ToApplyStep(skipped.ApplyResult));
                    await _sessionStore.SaveAsync(session, CancellationToken.None);
                    continue;
                }

                ActionCaptureResult capture;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    capture = await handler.CaptureAsync(context, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    capture = ActionCaptureResult.Skip(ActionExecutionStatus.Cancelled, "操作をキャンセルしました。");
                }
                catch (Exception ex)
                {
                    capture = ActionCaptureResult.Skip(ActionExecutionStatus.ApplyFailed, "現在状態を記録できないため変更しません。", ex.ToString());
                }

                var action = new ActionSnapshot
                {
                    ActionId = handler.Id,
                    OriginalState = capture.State,
                    StateCaptured = capture.CanApply
                };
                session.Actions.Add(action);
                var beforeSave = await _sessionStore.SaveAsync(session, CancellationToken.None);
                if (!beforeSave.IsSuccess)
                {
                    action.ApplyResult = ActionResults.Create(handler, ActionExecutionStatus.ApplyFailed, beforeSave.UserMessage, beforeSave.TechnicalDetails);
                    output.Add(ToApplyStep(action.ApplyResult));
                    break;
                }

                if (!capture.CanApply)
                {
                    action.ApplyResult = ActionResults.Create(handler, capture.Status, capture.Message, capture.TechnicalDetails);
                }
                else
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        action.ApplyResult = await handler.ApplyAsync(context, action, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        action.ApplyResult = ActionResults.Create(handler, ActionExecutionStatus.Cancelled, "操作をキャンセルしました。");
                    }
                    catch (Exception ex)
                    {
                        action.ApplyResult = ActionResults.Create(handler, ActionExecutionStatus.ApplyFailed, "適用中に予期しない問題が発生しました。", ex.ToString());
                    }
                }

                output.Add(ToApplyStep(action.ApplyResult));
                await _logger.WriteAsync(
                    action.ApplyResult.Status == ActionExecutionStatus.Succeeded ? "INFO" : "WARN",
                    session.SessionId, session.ModeId, handler.Id,
                    action.ApplyResult.Status.ToString(), action.ApplyResult.TechnicalDetails);
                var afterSave = await _sessionStore.SaveAsync(session, CancellationToken.None);
                if (!afterSave.IsSuccess)
                {
                    output.Add(new ApplyStepResult("元に戻すための記録", false, afterSave.UserMessage, afterSave.TechnicalDetails));
                    break;
                }
                if (action.ApplyResult.Status == ActionExecutionStatus.Cancelled)
                    break;
            }

            session.IsApplying = false;
            session.IsAwaitingRestore = session.Actions.Any(action =>
                action.StateCaptured && action.ApplyResult?.Status is ActionExecutionStatus.Succeeded or ActionExecutionStatus.ApplyFailed);
            await _sessionStore.SaveAsync(session, CancellationToken.None);
            if (!session.IsAwaitingRestore)
            {
                _sessionStore.Delete();
                _activeSession = null;
                _activeMode = null;
            }
            else if (mode.MonitorRules.Count > 0)
            {
                _monitor.Start(
                    mode.MonitorRules,
                    session.LaunchedProcesses,
                    async () => await RestoreAsync());
            }
            OnSessionChanged();
            return new ModeApplyResult { Steps = output };
        }
        catch (OperationCanceledException)
        {
            return SingleFailure("キャンセル", "モード適用をキャンセルしました。");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ModeApplyResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var session = _activeSession;
            if (session is null)
            {
                var load = await _sessionStore.LoadAsync(cancellationToken);
                if (!load.IsSuccess)
                    return SingleFailure("元に戻す", load.UserMessage, load.TechnicalDetails);
                session = load.Value;
            }
            if (session is null)
                return new ModeApplyResult
                {
                    Steps = [new ApplyStepResult("元に戻す", true, "元に戻すモードはありません。", IsSkipped: true)]
                };
            return await RestoreCoreAsync(session, _activeMode, CancellationToken.None);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public OperationResult IgnoreIncompleteSession()
    {
        _monitor.Stop();
        _activeSession = null;
        _activeMode = null;
        var result = _sessionStore.Ignore();
        OnSessionChanged();
        return result;
    }

    private async Task<ModeApplyResult> RestoreCoreAsync(
        ModeSessionSnapshot session,
        PcMode? mode,
        CancellationToken cancellationToken)
    {
        _monitor.Stop();
        var context = new ModeActionContext
        {
            Mode = mode ?? new PcMode { Id = session.ModeId, Name = session.ModeName },
            Session = session
        };
        var results = new List<ApplyStepResult>();
        foreach (var action in session.Actions.AsEnumerable().Reverse())
        {
            if (!action.StateCaptured || action.Restored)
                continue;
            var handler = _handlers.FirstOrDefault(value => value.Id == action.ActionId);
            if (handler is null)
            {
                action.RestoreResult = new ActionExecutionResult
                {
                    ActionId = action.ActionId,
                    DisplayName = action.ActionId,
                    Status = ActionExecutionStatus.RestoreFailed,
                    Message = "このバージョンでは、この項目を元に戻せません。"
                };
            }
            else
            {
                try
                {
                    action.RestoreResult = await handler.RestoreAsync(context, action, cancellationToken);
                }
                catch (Exception ex)
                {
                    action.RestoreResult = ActionResults.Create(handler, ActionExecutionStatus.RestoreFailed, "元に戻す途中で予期しない問題が発生しました。", ex.ToString());
                }
            }
            action.Restored = action.RestoreResult.Status == ActionExecutionStatus.RestoreSucceeded;
            results.Add(ToRestoreStep(action.RestoreResult));
            await _logger.WriteAsync(
                action.Restored ? "INFO" : "ERROR",
                session.SessionId, session.ModeId, action.ActionId,
                action.RestoreResult.Status.ToString(), action.RestoreResult.TechnicalDetails);
            await _sessionStore.SaveAsync(session, CancellationToken.None);
        }

        var failures = session.Actions.Count(action => action.StateCaptured && !action.Restored);
        session.IsApplying = false;
        session.IsAwaitingRestore = failures > 0;
        await _sessionStore.SaveAsync(session, CancellationToken.None);
        if (failures == 0)
        {
            _sessionStore.Delete();
            _activeSession = null;
            _activeMode = null;
        }
        else
        {
            _activeSession = session;
        }
        OnSessionChanged();
        if (results.Count == 0)
            results.Add(new ApplyStepResult("元に戻す", true, "すでに元に戻しています。", IsSkipped: true));
        return new ModeApplyResult { Steps = results };
    }

    private static async Task<ActionPreflightResult> SafePreflightAsync(
        IModeActionHandler handler,
        ModeActionContext context,
        CancellationToken cancellationToken)
    {
        try { return await handler.PreflightAsync(context, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ActionPreflightResult.Skip(ActionExecutionStatus.UnsupportedSkipped, $"{handler.DisplayName}の対応状況を確認できません。", ex.ToString()); }
    }

    private static ApplyStepResult ToApplyStep(ActionExecutionResult result)
    {
        var skipped = result.Status is ActionExecutionStatus.UnsupportedSkipped or
            ActionExecutionStatus.TargetNotFoundSkipped or ActionExecutionStatus.UserSkipped;
        return new ApplyStepResult(result.ActionId,
            result.Status == ActionExecutionStatus.Succeeded || skipped,
            result.Message, result.TechnicalDetails, skipped, result.DisplayName);
    }

    private static ApplyStepResult ToRestoreStep(ActionExecutionResult result) =>
        new(result.ActionId,
            result.Status == ActionExecutionStatus.RestoreSucceeded,
            result.Message, result.TechnicalDetails, false, result.DisplayName);

    private static ModeApplyResult SingleFailure(string name, string message, string? details = null) =>
        new() { Steps = [new ApplyStepResult(name, false, message, details)] };

    private void OnSessionChanged() => SessionChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.Dispose();
        foreach (var handler in _handlers.OfType<IDisposable>()) handler.Dispose();
        _operationGate.Dispose();
    }
}
