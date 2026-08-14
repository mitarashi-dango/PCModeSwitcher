using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class PowerPlanActionHandler : IModeActionHandler
{
    private readonly IPowerPolicyAccessor _policy;
    public string Id => "power-plan";
    public string DisplayName => "電源プラン";

    public PowerPlanActionHandler() : this(new PowerPolicyAccessor()) { }
    internal PowerPlanActionHandler(IPowerPolicyAccessor policy) => _policy = policy;

    public Task<ActionPreflightResult> PreflightAsync(
        ModeActionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Mode.Power.ChangePowerPlan)
            return Task.FromResult(ActionPreflightResult.Skip(
                ActionExecutionStatus.UserSkipped, "ユーザー設定により変更しません。"));
        if (context.Mode.Power.PowerPlanId == Guid.Empty)
            return Task.FromResult(ActionPreflightResult.Skip(
                ActionExecutionStatus.UserSkipped, "電源プランが選択されていません。"));

        var schemes = _policy.GetSchemes();
        if (!schemes.IsSuccess || schemes.Value is null)
            return Task.FromResult(ActionPreflightResult.Skip(
                ActionExecutionStatus.UnsupportedSkipped,
                schemes.UserMessage,
                schemes.TechnicalDetails));
        return Task.FromResult(schemes.Value.Any(plan => plan.Id == context.Mode.Power.PowerPlanId)
            ? ActionPreflightResult.Ready()
            : ActionPreflightResult.Skip(
                ActionExecutionStatus.TargetNotFoundSkipped,
                "選択した電源プランは現在のPCに存在しません。"));
    }

    public Task<ActionCaptureResult> CaptureAsync(
        ModeActionContext context,
        CancellationToken cancellationToken)
    {
        var current = _policy.GetActiveScheme();
        return Task.FromResult(current.IsSuccess
            ? ActionCaptureResult.Success(new PowerPlanSnapshot(current.Value))
            : ActionCaptureResult.Skip(
                ActionExecutionStatus.ApplyFailed,
                "現在の電源プランを記録できないため変更しません。",
                current.TechnicalDetails));
    }

    public Task<ActionExecutionResult> ApplyAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var requested = context.Mode.Power.PowerPlanId;
        var result = _policy.ActivateScheme(requested);
        if (result.IsSuccess)
        {
            var verify = _policy.GetActiveScheme();
            result = verify.IsSuccess && verify.Value == requested
                ? OperationResult.Success("電源プランを切り替えました。")
                : OperationResult.Failure("電源プランの切り替えを確認できませんでした。", verify.TechnicalDetails);
        }

        return Task.FromResult(ActionResults.Create(
            this,
            result.IsSuccess ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.ApplyFailed,
            result.UserMessage,
            result.TechnicalDetails));
    }

    public Task<ActionExecutionResult> RestoreAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var state = snapshot.OriginalState.Deserialize<PowerPlanSnapshot>();
        if (state is null)
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.RestoreFailed, "元に戻すための情報がありません。"));
        var result = _policy.ActivateScheme(state.ActiveSchemeId);
        return Task.FromResult(ActionResults.Create(
            this,
            result.IsSuccess ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed,
            result.IsSuccess ? "元の電源プランへ戻しました。" : result.UserMessage,
            result.TechnicalDetails));
    }

    private sealed record PowerPlanSnapshot(Guid ActiveSchemeId);
}

public sealed class PowerTimeoutActionHandler : IModeActionHandler
{
    private readonly IPowerPolicyAccessor _policy;
    private readonly Func<bool> _hasBattery;
    public string Id => "power-timeouts";
    public string DisplayName => "画面OFF・スリープ時間";

    public PowerTimeoutActionHandler() : this(new PowerPolicyAccessor(), DetectBattery) { }
    internal PowerTimeoutActionHandler(IPowerPolicyAccessor policy, Func<bool>? hasBattery = null)
    {
        _policy = policy;
        _hasBattery = hasBattery ?? (() => false);
    }

    public Task<ActionPreflightResult> PreflightAsync(
        ModeActionContext context,
        CancellationToken cancellationToken)
    {
        var power = context.Mode.Power;
        var values = new[]
        {
            power.DisplayTimeoutAcSeconds, power.DisplayTimeoutDcSeconds,
            power.SleepTimeoutAcSeconds, power.SleepTimeoutDcSeconds
        };
        if (values.All(value => value is null))
            return Task.FromResult(ActionPreflightResult.Skip(
                ActionExecutionStatus.UserSkipped, "すべて「変更しない」です。"));
        if (values.Any(value => value > PowerSettingsService.MaximumTimeoutSeconds))
            return Task.FromResult(ActionPreflightResult.Fatal("画面OFFまたはスリープ時間が正しくありません。"));
        return Task.FromResult(ActionPreflightResult.Ready());
    }

    public Task<ActionCaptureResult> CaptureAsync(
        ModeActionContext context,
        CancellationToken cancellationToken)
    {
        var active = _policy.GetActiveScheme();
        if (!active.IsSuccess)
            return Task.FromResult(ActionCaptureResult.Skip(
                ActionExecutionStatus.ApplyFailed,
                "現在の電源プランを確認できないため変更しません。",
                active.TechnicalDetails));

        var target = context.Mode.Power.ChangePowerPlan
            ? context.Mode.Power.PowerPlanId
            : active.Value;
        var displayAc = _policy.ReadValue(target, PowerSettingsService.VideoSubgroupId, PowerSettingsService.DisplayTimeoutId, PowerSource.Ac);
        var displayDc = _policy.ReadValue(target, PowerSettingsService.VideoSubgroupId, PowerSettingsService.DisplayTimeoutId, PowerSource.Dc);
        var sleepAc = _policy.ReadValue(target, PowerSettingsService.SleepSubgroupId, PowerSettingsService.SleepTimeoutId, PowerSource.Ac);
        var sleepDc = _policy.ReadValue(target, PowerSettingsService.SleepSubgroupId, PowerSettingsService.SleepTimeoutId, PowerSource.Dc);
        var reads = new[] { displayAc, displayDc, sleepAc, sleepDc };
        if (reads.Any(result => !result.IsSuccess))
            return Task.FromResult(ActionCaptureResult.Skip(
                ActionExecutionStatus.ApplyFailed,
                "変更先電源プランの現在値を記録できないため変更しません。",
                string.Join("; ", reads.Where(result => !result.IsSuccess)
                    .Select(result => result.TechnicalDetails ?? result.UserMessage))));

        return Task.FromResult(ActionCaptureResult.Success(new TimeoutSnapshot(
            target, displayAc.Value, displayDc.Value, sleepAc.Value, sleepDc.Value)));
    }

    public Task<ActionExecutionResult> ApplyAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var state = snapshot.OriginalState.Deserialize<TimeoutSnapshot>();
        if (state is null)
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.ApplyFailed, "適用前の値がありません。"));
        var power = context.Mode.Power;
        var writes = new List<OperationResult>();
        WriteIfSet(writes, state.SchemeId, PowerSettingsService.VideoSubgroupId, PowerSettingsService.DisplayTimeoutId, PowerSource.Ac, power.DisplayTimeoutAcSeconds);
        if (_hasBattery())
            WriteIfSet(writes, state.SchemeId, PowerSettingsService.VideoSubgroupId, PowerSettingsService.DisplayTimeoutId, PowerSource.Dc, power.DisplayTimeoutDcSeconds);
        WriteIfSet(writes, state.SchemeId, PowerSettingsService.SleepSubgroupId, PowerSettingsService.SleepTimeoutId, PowerSource.Ac, power.SleepTimeoutAcSeconds);
        if (_hasBattery())
            WriteIfSet(writes, state.SchemeId, PowerSettingsService.SleepSubgroupId, PowerSettingsService.SleepTimeoutId, PowerSource.Dc, power.SleepTimeoutDcSeconds);
        writes.Add(_policy.ActivateScheme(state.SchemeId));
        var failed = writes.Where(result => !result.IsSuccess).ToList();
        return Task.FromResult(ActionResults.Create(
            this,
            failed.Count == 0 ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.ApplyFailed,
            failed.Count == 0 ? "画面OFF・スリープ時間を変更しました。" : "一部の電源時間を変更できませんでした。",
            JoinErrors(failed)));
    }

    public Task<ActionExecutionResult> RestoreAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var state = snapshot.OriginalState.Deserialize<TimeoutSnapshot>();
        if (state is null)
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.RestoreFailed, "元に戻すための情報がありません。"));
        var results = new[]
        {
            _policy.WriteValue(state.SchemeId, PowerSettingsService.VideoSubgroupId, PowerSettingsService.DisplayTimeoutId, PowerSource.Ac, state.DisplayAc),
            _policy.WriteValue(state.SchemeId, PowerSettingsService.VideoSubgroupId, PowerSettingsService.DisplayTimeoutId, PowerSource.Dc, state.DisplayDc),
            _policy.WriteValue(state.SchemeId, PowerSettingsService.SleepSubgroupId, PowerSettingsService.SleepTimeoutId, PowerSource.Ac, state.SleepAc),
            _policy.WriteValue(state.SchemeId, PowerSettingsService.SleepSubgroupId, PowerSettingsService.SleepTimeoutId, PowerSource.Dc, state.SleepDc),
            _policy.ActivateScheme(state.SchemeId)
        };
        var failed = results.Where(result => !result.IsSuccess).ToList();
        return Task.FromResult(ActionResults.Create(
            this,
            failed.Count == 0 ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed,
            failed.Count == 0 ? "変更先プランの画面OFF・スリープ時間を戻しました。" : "一部の電源設定を元に戻せませんでした。",
            JoinErrors(failed)));
    }

    private void WriteIfSet(
        List<OperationResult> results, Guid scheme, Guid subgroup, Guid setting,
        PowerSource source, uint? value)
    {
        if (value is not null)
            results.Add(_policy.WriteValue(scheme, subgroup, setting, source, value.Value));
    }

    private static string? JoinErrors(IEnumerable<OperationResult> results)
    {
        var value = string.Join("; ", results.Select(result => result.TechnicalDetails ?? result.UserMessage));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool DetectBattery() =>
        GetSystemPowerStatus(out var status) && status.BatteryFlag != 255 && (status.BatteryFlag & 128) == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private sealed record TimeoutSnapshot(
        Guid SchemeId, uint DisplayAc, uint DisplayDc, uint SleepAc, uint SleepDc);
}

public sealed class WindowsPowerModeActionHandler : IModeActionHandler
{
    private static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid Balanced = Guid.Empty;
    private static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public string Id => "windows-power-mode";
    public string DisplayName => "Windowsへ要求する電源モード";

    public Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        if (context.Mode.Power.AcPowerMode == WindowsPowerMode.NoChange &&
            context.Mode.Power.DcPowerMode == WindowsPowerMode.NoChange)
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UserSkipped, "変更しない設定です。"));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UnsupportedSkipped, "Windows 11でのみ利用できます。"));
        try
        {
            var status = PowerGetUserConfiguredACPowerMode(out _);
            return Task.FromResult(status == 0
                ? ActionPreflightResult.Ready()
                : ActionPreflightResult.Skip(ActionExecutionStatus.UnsupportedSkipped, "このWindowsでは電源モードを変更できません。", Error(status)));
        }
        catch (EntryPointNotFoundException ex)
        {
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UnsupportedSkipped, "このWindowsでは電源モードを変更できません。", ex.Message));
        }
    }

    public Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var acStatus = PowerGetUserConfiguredACPowerMode(out var ac);
            var dcStatus = PowerGetUserConfiguredDCPowerMode(out var dc);
            return Task.FromResult(acStatus == 0 && dcStatus == 0
                ? ActionCaptureResult.Success(new PowerModeSnapshot(ac, dc))
                : ActionCaptureResult.Skip(ActionExecutionStatus.ApplyFailed, "現在のWindows電源モードを記録できないため変更しません。", $"AC={Error(acStatus)}; DC={Error(dcStatus)}"));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return Task.FromResult(ActionCaptureResult.Skip(ActionExecutionStatus.UnsupportedSkipped, "このWindowsでは電源モードを変更できません。", ex.Message));
        }
    }

    public Task<ActionExecutionResult> ApplyAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (context.Mode.Power.AcPowerMode != WindowsPowerMode.NoChange)
        {
            var guid = ToGuid(context.Mode.Power.AcPowerMode);
            var status = PowerSetUserConfiguredACPowerMode(ref guid);
            if (status != 0) errors.Add($"AC: {Error(status)}");
        }
        if (context.Mode.Power.DcPowerMode != WindowsPowerMode.NoChange)
        {
            var guid = ToGuid(context.Mode.Power.DcPowerMode);
            var status = PowerSetUserConfiguredDCPowerMode(ref guid);
            if (status != 0) errors.Add($"DC: {Error(status)}");
        }
        return Task.FromResult(ActionResults.Create(this,
            errors.Count == 0 ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.ApplyFailed,
            errors.Count == 0 ? "Windowsへ電源モードを要求しました。" : "電源モードを要求できませんでした。",
            errors.Count == 0 ? null : string.Join("; ", errors)));
    }

    public Task<ActionExecutionResult> RestoreAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var state = snapshot.OriginalState.Deserialize<PowerModeSnapshot>();
        if (state is null)
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.RestoreFailed, "元に戻すための情報がありません。"));
        var ac = state.Ac;
        var dc = state.Dc;
        var acStatus = PowerSetUserConfiguredACPowerMode(ref ac);
        var dcStatus = PowerSetUserConfiguredDCPowerMode(ref dc);
        return Task.FromResult(ActionResults.Create(this,
            acStatus == 0 && dcStatus == 0 ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed,
            acStatus == 0 && dcStatus == 0 ? "Windows電源モードを戻しました。" : "Windowsの電源モードを元に戻せませんでした。",
            acStatus == 0 && dcStatus == 0 ? null : $"AC={Error(acStatus)}; DC={Error(dcStatus)}"));
    }

    private static Guid ToGuid(WindowsPowerMode value) => value switch
    {
        WindowsPowerMode.BestEfficiency => BestEfficiency,
        WindowsPowerMode.Balanced => Balanced,
        WindowsPowerMode.BestPerformance => BestPerformance,
        _ => Balanced
    };

    private static string Error(uint status) => status == 0 ? "success" : $"0x{status:X8} {new Win32Exception(unchecked((int)status)).Message}";

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid powerModeGuid);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid powerModeGuid);

    private sealed record PowerModeSnapshot(Guid Ac, Guid Dc);
}

public sealed class PowerRequestActionHandler : IModeActionHandler, IDisposable
{
    private SafePowerRequestHandle? _handle;
    private readonly HashSet<PowerRequestType> _activeRequests = [];
    public string Id => "power-request";
    public string DisplayName => "一時的なスリープ防止";

    public Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Mode.Power.SleepPrevention == SleepPreventionMode.None
            ? ActionPreflightResult.Skip(ActionExecutionStatus.UserSkipped, "使用しない設定です。")
            : ActionPreflightResult.Ready());

    public Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ActionCaptureResult.Success(new { active = false }));

    public Task<ActionExecutionResult> ApplyAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        ReleaseRequests();
        var reason = new ReasonContext
        {
            Version = 0,
            Flags = 1,
            SimpleReasonString = Marshal.StringToHGlobalUni($"PCModeSwitcher: {context.Mode.Name}")
        };
        try
        {
            _handle = PowerCreateRequest(ref reason);
            if (_handle.IsInvalid)
                return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.ApplyFailed, "一時的なスリープ防止を開始できませんでした。", new Win32Exception().Message));
            var requested = new List<PowerRequestType> { PowerRequestType.SystemRequired };
            if (context.Mode.Power.SleepPrevention == SleepPreventionMode.SystemAndDisplay)
                requested.Add(PowerRequestType.DisplayRequired);
            foreach (var request in requested)
            {
                if (!PowerSetRequest(_handle, request))
                {
                    var error = new Win32Exception().Message;
                    ReleaseRequests();
                    return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.ApplyFailed, "一時的なスリープ防止を設定できませんでした。", error));
                }
                _activeRequests.Add(request);
            }
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.Succeeded, "自動スリープを一時的に防止しました。"));
        }
        finally
        {
            Marshal.FreeHGlobal(reason.SimpleReasonString);
        }
    }

    public Task<ActionExecutionResult> RestoreAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var success = ReleaseRequests();
        return Task.FromResult(ActionResults.Create(this,
            success ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed,
            success ? "一時的なスリープ防止を解除しました。" : "一時的なスリープ防止を完全には解除できませんでした。"));
    }

    private bool ReleaseRequests()
    {
        var success = true;
        if (_handle is not null && !_handle.IsInvalid)
        {
            foreach (var request in _activeRequests)
                success &= PowerClearRequest(_handle, request);
        }
        _activeRequests.Clear();
        _handle?.Dispose();
        _handle = null;
        return success;
    }

    public void Dispose() => ReleaseRequests();

    private enum PowerRequestType { DisplayRequired, SystemRequired, AwayModeRequired, ExecutionRequired }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    private sealed class SafePowerRequestHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafePowerRequestHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafePowerRequestHandle PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerSetRequest(SafePowerRequestHandle handle, PowerRequestType requestType);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerClearRequest(SafePowerRequestHandle handle, PowerRequestType requestType);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
