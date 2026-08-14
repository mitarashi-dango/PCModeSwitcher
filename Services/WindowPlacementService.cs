using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PCModeSwitcher.Models;
using Forms = System.Windows.Forms;

namespace PCModeSwitcher.Services;

public sealed class WindowPlacementService
{
    public OperationResult<IReadOnlyList<WindowPlacementRule>> CaptureVisibleWindows()
    {
        try
        {
            var windows = EnumerateWindows();
            var rules = windows.Select(window => new WindowPlacementRule
            {
                ExecutablePath = window.ExecutablePath,
                ProcessName = window.ProcessName,
                WindowClassName = window.ClassName,
                TitleContains = window.Title,
                MonitorDeviceName = window.MonitorDeviceName,
                Placement = window.Placement
            }).ToList();
            return OperationResult<IReadOnlyList<WindowPlacementRule>>.Success(rules);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<WindowPlacementRule>>.Failure("現在のウィンドウ配置を取得できませんでした。", ex.ToString());
        }
    }

    internal IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var values = new List<WindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0)
                return true;
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId)
                return true;
            try
            {
                using var process = Process.GetProcessById(unchecked((int)processId));
                var executablePath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executablePath)) return true;
                var title = ReadText(handle);
                var className = ReadClassName(handle);
                var placement = NativeWindowPlacement.Create();
                if (!GetWindowPlacement(handle, ref placement)) return true;
                var monitor = MonitorFromWindow(handle, 2);
                var monitorInfo = MonitorInfo.Create();
                var monitorName = GetMonitorInfo(monitor, ref monitorInfo)
                    ? monitorInfo.DeviceName : null;
                values.Add(new WindowInfo(
                    handle,
                    executablePath,
                    process.ProcessName,
                    className,
                    title,
                    monitorName,
                    FromNative(placement)));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 権限差または終了中のウィンドウは対象外。
            }
            return true;
        }, IntPtr.Zero);
        return values;
    }

    internal OperationResult Apply(WindowInfo window, WindowPlacementData requested, string? monitorDeviceName)
    {
        try
        {
            var adjusted = ClampToVisibleWorkArea(requested, monitorDeviceName);
            var placement = ToNative(adjusted);
            return SetWindowPlacement(window.Handle, ref placement)
                ? OperationResult.Success("ウィンドウ配置を適用しました。")
                : OperationResult.Failure("ウィンドウを移動できませんでした。", new System.ComponentModel.Win32Exception().Message);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure("ウィンドウを移動できませんでした。", ex.ToString());
        }
    }

    internal WindowInfo? Find(WindowPlacementRule rule) =>
        EnumerateWindows().FirstOrDefault(window => Matches(window, rule));

    internal static bool Matches(WindowInfo window, WindowPlacementRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.ExecutablePath) &&
            !string.Equals(window.ExecutablePath, rule.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(rule.ProcessName) &&
            !string.Equals(window.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(rule.WindowClassName) &&
            !string.Equals(window.ClassName, rule.WindowClassName, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(rule.TitleContains) &&
            !window.Title.Contains(rule.TitleContains, StringComparison.CurrentCultureIgnoreCase)) return false;
        return true;
    }

    private static WindowPlacementData ClampToVisibleWorkArea(
        WindowPlacementData source,
        string? monitorDeviceName)
    {
        var screen = !string.IsNullOrWhiteSpace(monitorDeviceName)
            ? Forms.Screen.AllScreens.FirstOrDefault(value =>
                string.Equals(value.DeviceName, monitorDeviceName, StringComparison.OrdinalIgnoreCase))
            : null;
        screen ??= Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First();
        var area = screen.WorkingArea;
        var width = Math.Clamp(source.NormalRight - source.NormalLeft, 100, Math.Max(100, area.Width));
        var height = Math.Clamp(source.NormalBottom - source.NormalTop, 100, Math.Max(100, area.Height));
        var left = Math.Clamp(source.NormalLeft, area.Left, area.Right - width);
        var top = Math.Clamp(source.NormalTop, area.Top, area.Bottom - height);
        return new WindowPlacementData
        {
            ShowCommand = source.ShowCommand,
            NormalLeft = left,
            NormalTop = top,
            NormalRight = left + width,
            NormalBottom = top + height
        };
    }

    private static WindowPlacementData FromNative(NativeWindowPlacement placement) => new()
    {
        ShowCommand = placement.ShowCommand,
        NormalLeft = placement.NormalPosition.Left,
        NormalTop = placement.NormalPosition.Top,
        NormalRight = placement.NormalPosition.Right,
        NormalBottom = placement.NormalPosition.Bottom
    };

    private static NativeWindowPlacement ToNative(WindowPlacementData value) => new()
    {
        Length = Marshal.SizeOf<NativeWindowPlacement>(),
        ShowCommand = value.ShowCommand,
        NormalPosition = new NativeRect
        {
            Left = value.NormalLeft,
            Top = value.NormalTop,
            Right = value.NormalRight,
            Bottom = value.NormalBottom
        }
    };

    private static string ReadText(IntPtr handle)
    {
        var buffer = new StringBuilder(GetWindowTextLength(handle) + 1);
        _ = GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ReadClassName(IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassName(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    internal sealed record WindowInfo(
        IntPtr Handle,
        string ExecutablePath,
        string ProcessName,
        string ClassName,
        string Title,
        string? MonitorDeviceName,
        WindowPlacementData Placement);

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length, Flags, ShowCommand;
        public NativePoint MinPosition, MaxPosition;
        public NativeRect NormalPosition;
        public static NativeWindowPlacement Create() => new() { Length = Marshal.SizeOf<NativeWindowPlacement>() };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public static MonitorInfo Create() => new() { Size = Marshal.SizeOf<MonitorInfo>(), DeviceName = "" };
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr handle, ref NativeWindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr handle, ref NativeWindowPlacement placement);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

public sealed class WindowPlacementActionHandler : IModeActionHandler
{
    private readonly WindowPlacementService _service;
    public string Id => "window-placement";
    public string DisplayName => "ウィンドウ配置";

    public WindowPlacementActionHandler(WindowPlacementService? service = null) =>
        _service = service ?? new WindowPlacementService();

    public Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        if (context.Mode.WindowPlacements.Count == 0)
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UserSkipped, "保存済みの配置がありません。"));
        if (context.Mode.WindowPlacements.Any(rule =>
            string.IsNullOrWhiteSpace(rule.ExecutablePath) &&
            string.IsNullOrWhiteSpace(rule.ProcessName) &&
            string.IsNullOrWhiteSpace(rule.WindowClassName) &&
            string.IsNullOrWhiteSpace(rule.TitleContains)))
            return Task.FromResult(ActionPreflightResult.Fatal("識別条件のないウィンドウ配置があります。"));
        return Task.FromResult(ActionPreflightResult.Ready());
    }

    public Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        var originals = new List<CapturedWindow>();
        foreach (var rule in context.Mode.WindowPlacements)
        {
            var window = _service.Find(rule);
            if (window is null) continue;
            originals.Add(new CapturedWindow
            {
                Rule = new WindowPlacementRule
                {
                    ExecutablePath = window.ExecutablePath,
                    ProcessName = window.ProcessName,
                    WindowClassName = window.ClassName,
                    TitleContains = window.Title,
                    MonitorDeviceName = window.MonitorDeviceName,
                    Placement = window.Placement.Copy()
                }
            });
        }
        return Task.FromResult(ActionCaptureResult.Success(new WindowSnapshot { Windows = originals }));
    }

    public async Task<ActionExecutionResult> ApplyAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var pending = context.Mode.WindowPlacements.ToList();
        var errors = new List<string>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var rule in pending.ToList())
            {
                var window = _service.Find(rule);
                if (window is null) continue;
                var result = _service.Apply(window, rule.Placement, rule.MonitorDeviceName);
                if (!result.IsSuccess) errors.Add(result.TechnicalDetails ?? result.UserMessage);
                pending.Remove(rule);
            }
            if (pending.Count > 0) await Task.Delay(500, cancellationToken);
        }
        errors.AddRange(pending.Select(rule => $"対象ウィンドウが見つかりません: {rule.TitleContains ?? rule.ProcessName ?? rule.ExecutablePath}"));
        return ActionResults.Create(this,
            errors.Count == 0 ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.ApplyFailed,
            errors.Count == 0 ? "ウィンドウ配置を適用しました。" : "一部のウィンドウを配置できませんでした。",
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    public Task<ActionExecutionResult> RestoreAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var state = snapshot.OriginalState.Deserialize<WindowSnapshot>();
        if (state is null)
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.RestoreFailed, "元に戻すための情報がありません。"));
        var errors = new List<string>();
        foreach (var captured in state.Windows)
        {
            var window = _service.Find(captured.Rule);
            if (window is null) continue;
            var result = _service.Apply(window, captured.Rule.Placement, captured.Rule.MonitorDeviceName);
            if (!result.IsSuccess) errors.Add(result.TechnicalDetails ?? result.UserMessage);
        }
        return Task.FromResult(ActionResults.Create(this,
            errors.Count == 0 ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed,
            errors.Count == 0 ? "元のウィンドウ配置へ戻しました。" : "一部のウィンドウ配置を元に戻せませんでした。",
            errors.Count == 0 ? null : string.Join("; ", errors)));
    }

    private sealed class CapturedWindow { public WindowPlacementRule Rule { get; set; } = new(); }
    private sealed class WindowSnapshot { public List<CapturedWindow> Windows { get; set; } = []; }
}
