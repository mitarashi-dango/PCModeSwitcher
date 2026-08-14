using System.Diagnostics;
using System.IO;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class ProcessActionHandler : IModeActionHandler
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "winlogon", "csrss", "lsass", "services", "smss", "system", "registry"
    };

    public string Id => "processes";
    public string DisplayName => "アプリの終了・起動";

    public Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        if (context.Mode.LaunchItems.Count == 0 && context.Mode.CloseProcessRules.Count == 0)
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UserSkipped, "登録項目がありません。"));
        foreach (var rule in context.Mode.CloseProcessRules)
        {
            if (!IsSafeCloseTarget(rule.ExecutablePath, out var reason))
                return Task.FromResult(ActionPreflightResult.Fatal(reason));
            if (rule.GracePeriodSeconds is < 0 or > 300)
                return Task.FromResult(ActionPreflightResult.Fatal("アプリ終了の待機時間は0～300秒で指定してください。"));
        }
        return Task.FromResult(ActionPreflightResult.Ready());
    }

    public Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ActionCaptureResult.Success(new { capturedUtc = DateTimeOffset.UtcNow }));

    public async Task<ActionExecutionResult> ApplyAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var rule in context.Mode.CloseProcessRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = FindByPath(rule.ExecutablePath);
            foreach (var process in matches)
            {
                using (process)
                {
                    var identity = TryGetIdentity(process);
                    if (identity is null)
                    {
                        errors.Add($"{rule.ExecutablePath}: プロセス情報を確認できません。");
                        continue;
                    }
                    try
                    {
                        var closeRequested = process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow();
                        var exited = closeRequested && await WaitForExitAsync(
                            process,
                            TimeSpan.FromSeconds(rule.GracePeriodSeconds),
                            cancellationToken);
                        if (!exited && rule.AllowForceKill)
                        {
                            process.Kill(entireProcessTree: false);
                            exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(5), cancellationToken);
                        }
                        if (!exited)
                        {
                            errors.Add(closeRequested
                                ? $"{Path.GetFileName(rule.ExecutablePath)}: 指定時間内に終了しませんでした。"
                                : $"{Path.GetFileName(rule.ExecutablePath)}: 通常終了を要求できませんでした。");
                            continue;
                        }
                        context.Session.ClosedProcesses.Add(new ClosedProcessRecord
                        {
                            ProcessId = identity.ProcessId,
                            StartTimeUtc = identity.StartTimeUtc,
                            ExecutablePath = identity.ExecutablePath,
                            RestartOnRestore = rule.RestartOnRestore,
                            Arguments = [.. rule.RestartArguments],
                            WorkingDirectory = rule.RestartWorkingDirectory,
                            RuleId = rule.Id
                        });
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        errors.Add($"{Path.GetFileName(rule.ExecutablePath)}: {ex.Message}");
                    }
                }
            }
        }

        foreach (var item in context.Mode.LaunchItems.OrderBy(item => item.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.DelayMilliseconds > 0)
                await Task.Delay(item.DelayMilliseconds, cancellationToken);
            if (!item.AllowAdditionalInstance && IsRunning(item.Target))
                continue;
            var launch = Start(item.Target, item.Arguments, item.WorkingDirectory);
            if (!launch.IsSuccess || launch.Value is null)
            {
                errors.Add($"{item.Target}: {launch.UserMessage}");
                continue;
            }
            using var process = launch.Value;
            var identity = TryGetIdentity(process);
            if (identity is null)
            {
                errors.Add($"{item.Target}: 起動したプロセスを追跡できません。");
                continue;
            }
            context.Session.LaunchedProcesses.Add(new TrackedProcess
            {
                ProcessId = identity.ProcessId,
                StartTimeUtc = identity.StartTimeUtc,
                ExecutablePath = identity.ExecutablePath,
                CloseOnRestore = item.CloseOnRestore,
                RuleId = item.Id
            });
        }

        return ActionResults.Create(
            this,
            errors.Count == 0 ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.ApplyFailed,
            errors.Count == 0 ? "登録したアプリ操作を実行しました。" : "一部のアプリ操作を実行できませんでした。",
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    public async Task<ActionExecutionResult> RestoreAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var tracked in context.Session.LaunchedProcesses.Where(value => value.CloseOnRestore))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = GetMatchingProcess(tracked.ProcessId, tracked.StartTimeUtc, tracked.ExecutablePath);
            if (process is null) continue;
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        process.CloseMainWindow();
                    if (!await WaitForExitAsync(process, TimeSpan.FromSeconds(5), cancellationToken))
                        errors.Add($"{Path.GetFileName(tracked.ExecutablePath)}: 通常終了しませんでした。");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    errors.Add($"{tracked.ExecutablePath}: {ex.Message}");
                }
            }
        }

        foreach (var closed in context.Session.ClosedProcesses.Where(value => value.RestartOnRestore))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var launch = Start(closed.ExecutablePath, closed.Arguments, closed.WorkingDirectory);
            if (!launch.IsSuccess)
                errors.Add($"{closed.ExecutablePath}: {launch.UserMessage}");
            launch.Value?.Dispose();
        }

        return ActionResults.Create(
            this,
            errors.Count == 0 ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed,
            errors.Count == 0 ? "起動・終了したアプリを可能な範囲で戻しました。" : "一部のアプリを元の状態へ戻せませんでした。",
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    internal static OperationResult<Process> Start(
        string target,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory)
    {
        try
        {
            var isShellTarget = IsShellTarget(target);
            if (!isShellTarget && !File.Exists(target))
                return OperationResult<Process>.Failure("実行ファイルが見つかりません。");
            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = isShellTarget,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? (!isShellTarget ? Path.GetDirectoryName(target) ?? "" : "")
                    : workingDirectory
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            var process = Process.Start(startInfo);
            return process is null
                ? OperationResult<Process>.Failure("プロセスを開始できませんでした。")
                : OperationResult<Process>.Success(process);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return OperationResult<Process>.Failure("プロセスを開始できませんでした。", ex.ToString());
        }
    }

    internal static bool IsRunning(string target)
    {
        if (IsShellTarget(target)) return false;
        var matches = FindByPath(target);
        try { return matches.Count > 0; }
        finally { foreach (var process in matches) process.Dispose(); }
    }

    internal static bool Matches(Process process, string pathOrName)
    {
        try
        {
            if (Path.IsPathFullyQualified(pathOrName))
                return string.Equals(process.MainModule?.FileName, Path.GetFullPath(pathOrName), StringComparison.OrdinalIgnoreCase);
            return string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(pathOrName), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static List<Process> FindByPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return Process.GetProcessesByName(name).Where(process => Matches(process, path)).ToList();
    }

    private static ProcessIdentity? TryGetIdentity(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path)) return null;
            return new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime(), Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static Process? GetMatchingProcess(int id, DateTimeOffset startUtc, string path)
    {
        try
        {
            var process = Process.GetProcessById(id);
            var identity = TryGetIdentity(process);
            if (identity is not null &&
                Math.Abs((identity.StartTimeUtc - startUtc).TotalSeconds) < 1 &&
                string.Equals(identity.ExecutablePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                return process;
            process.Dispose();
            return null;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken token)
    {
        if (process.HasExited) return true;
        var exitTask = process.WaitForExitAsync(token);
        var completed = await Task.WhenAny(exitTask, Task.Delay(timeout, token));
        return completed == exitTask && process.HasExited;
    }

    private static bool IsShellTarget(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile;

    private static bool IsSafeCloseTarget(string path, out string reason)
    {
        reason = "";
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            reason = $"終了対象の実行ファイルが見つかりません: {path}";
            return false;
        }
        var fullPath = Path.GetFullPath(path);
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (ProtectedNames.Contains(name) || fullPath.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Windowsの重要プロセスは終了対象に登録できません: {path}";
            return false;
        }
        if (string.Equals(fullPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            reason = "PCModeSwitcher自身は終了対象に登録できません。";
            return false;
        }
        return true;
    }

    private sealed record ProcessIdentity(int ProcessId, DateTimeOffset StartTimeUtc, string ExecutablePath);
}

public sealed class ProcessMonitorService : IDisposable
{
    private CancellationTokenSource? _source;
    private readonly List<Process> _eventProcesses = [];
    private TaskCompletionSource<bool> _processExited = CreateExitSignal();

    public void Start(
        IReadOnlyCollection<ProcessMonitorRule> rules,
        IReadOnlyCollection<TrackedProcess> launchedProcesses,
        Func<Task> onAllExited)
    {
        Stop();
        if (rules.Count == 0) return;
        _source = new CancellationTokenSource();
        foreach (var tracked in launchedProcesses)
        {
            if (!rules.Any(rule => PathMatches(tracked.ExecutablePath, rule.ExecutablePathOrName)))
                continue;
            try
            {
                var process = Process.GetProcessById(tracked.ProcessId);
                if (!ProcessActionHandler.Matches(process, tracked.ExecutablePath) ||
                    Math.Abs((process.StartTime.ToUniversalTime() - tracked.StartTimeUtc).TotalSeconds) >= 1)
                {
                    process.Dispose();
                    continue;
                }
                process.EnableRaisingEvents = true;
                process.Exited += OnTrackedProcessExited;
                _eventProcesses.Add(process);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 間接起動と同様にポーリングで監視を継続する。
            }
        }
        _ = MonitorAsync(rules.Select(rule => rule.Copy()).ToList(), onAllExited, _source.Token);
    }

    public void Stop()
    {
        _source?.Cancel();
        _source?.Dispose();
        _source = null;
        foreach (var process in _eventProcesses)
        {
            process.Exited -= OnTrackedProcessExited;
            process.Dispose();
        }
        _eventProcesses.Clear();
        Interlocked.Exchange(ref _processExited, CreateExitSignal());
    }

    private async Task MonitorAsync(
        IReadOnlyCollection<ProcessMonitorRule> rules,
        Func<Task> onAllExited,
        CancellationToken token)
    {
        var seenRunning = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var anyRunning = rules.Any(rule => IsRuleRunning(rule.ExecutablePathOrName));
                seenRunning |= anyRunning;
                if (seenRunning && !anyRunning)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    if (!rules.Any(rule => IsRuleRunning(rule.ExecutablePathOrName)))
                    {
                        await onAllExited();
                        return;
                    }
                }
                var exitSignal = _processExited;
                await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(1), token), exitSignal.Task);
                if (exitSignal.Task.IsCompleted)
                    Interlocked.CompareExchange(ref _processExited, CreateExitSignal(), exitSignal);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool IsRuleRunning(string pathOrName)
    {
        var name = Path.GetFileNameWithoutExtension(pathOrName);
        return Process.GetProcessesByName(name).Any(process =>
        {
            using (process) return ProcessActionHandler.Matches(process, pathOrName);
        });
    }

    private void OnTrackedProcessExited(object? sender, EventArgs e)
    {
        _processExited.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateExitSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool PathMatches(string executablePath, string rule) =>
        Path.IsPathFullyQualified(rule)
            ? string.Equals(executablePath, Path.GetFullPath(rule), StringComparison.OrdinalIgnoreCase)
            : string.Equals(Path.GetFileNameWithoutExtension(executablePath),
                Path.GetFileNameWithoutExtension(rule), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        Stop();
    }
}
