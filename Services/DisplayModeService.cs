using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed record DisplayModeInfo(
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    uint Width,
    uint Height,
    uint BitsPerPixel,
    uint Orientation,
    uint CurrentRefreshRate,
    IReadOnlyList<uint> SupportedRefreshRates,
    string HardwareSignature);

public sealed class DisplayModeService
{
    internal const uint CurrentSettings = 0xFFFFFFFF;
    private const uint AttachedToDesktop = 0x1;
    private const uint PrimaryDevice = 0x4;
    private const uint DmBitsPerPel = 0x00040000;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFlags = 0x00200000;
    private const uint DmDisplayFrequency = 0x00400000;
    private const uint DmDisplayOrientation = 0x00000080;
    private const uint CdsTest = 0x00000002;

    public OperationResult<IReadOnlyList<DisplayModeInfo>> GetDisplays()
    {
        try
        {
            var displays = new List<DisplayModeInfo>();
            for (uint index = 0; ; index++)
            {
                var device = DisplayDevice.Create();
                if (!EnumDisplayDevices(null, index, ref device, 0))
                    break;
                if ((device.StateFlags & AttachedToDesktop) == 0)
                    continue;
                var currentResult = GetCurrentMode(device.DeviceName);
                if (!currentResult.IsSuccess || currentResult.Value is null)
                    continue;
                var current = currentResult.Value;
                var rates = new SortedSet<uint>();
                for (uint modeIndex = 0; ; modeIndex++)
                {
                    var candidate = DevMode.Create();
                    if (!EnumDisplaySettingsEx(device.DeviceName, modeIndex, ref candidate, 0))
                        break;
                    if (candidate.PelsWidth == current.Width &&
                        candidate.PelsHeight == current.Height &&
                        candidate.BitsPerPel == current.BitsPerPel &&
                        candidate.DisplayOrientation == current.Orientation &&
                        candidate.DisplayFrequency > 0)
                    {
                        rates.Add(candidate.DisplayFrequency);
                    }
                }
                var signature = string.Join("|", device.DeviceName, device.DeviceId,
                    current.Width, current.Height, current.BitsPerPel,
                    string.Join(',', rates));
                displays.Add(new DisplayModeInfo(
                    device.DeviceName,
                    string.IsNullOrWhiteSpace(device.DeviceString) ? device.DeviceName : device.DeviceString,
                    (device.StateFlags & PrimaryDevice) != 0,
                    current.Width,
                    current.Height,
                    current.BitsPerPel,
                    current.Orientation,
                    current.RefreshRate,
                    rates.ToList(),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(signature)))));
            }
            return OperationResult<IReadOnlyList<DisplayModeInfo>>.Success(displays);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<DisplayModeInfo>>.Failure("接続中のモニターを取得できませんでした。", ex.ToString());
        }
    }

    internal OperationResult<DisplayState> GetCurrentMode(string deviceName)
    {
        var mode = DevMode.Create();
        if (!EnumDisplaySettingsEx(deviceName, CurrentSettings, ref mode, 0))
            return OperationResult<DisplayState>.Failure("現在のディスプレイモードを取得できませんでした。", new Win32Exception().Message);
        return OperationResult<DisplayState>.Success(DisplayState.From(deviceName, mode));
    }

    internal OperationResult ApplyRefreshRate(string deviceName, uint refreshRate, bool testOnly = false)
    {
        var current = DevMode.Create();
        if (!EnumDisplaySettingsEx(deviceName, CurrentSettings, ref current, 0))
            return OperationResult.Failure("現在のディスプレイモードを取得できませんでした。", new Win32Exception().Message);
        current.Fields = DmDisplayFrequency;
        current.DisplayFrequency = refreshRate;
        var status = ChangeDisplaySettingsEx(deviceName, ref current, IntPtr.Zero, testOnly ? CdsTest : 0, IntPtr.Zero);
        return status == 0
            ? OperationResult.Success(testOnly ? "ディスプレイ設定をテストしました。" : "リフレッシュレートを変更しました。")
            : OperationResult.Failure("リフレッシュレートを変更できませんでした。", $"ChangeDisplaySettingsEx={status}");
    }

    internal OperationResult Restore(DisplayState state)
    {
        var mode = state.ToDevMode();
        var status = ChangeDisplaySettingsEx(state.DeviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
        return status == 0
            ? OperationResult.Success("元のディスプレイモードへ戻しました。")
            : OperationResult.Failure("元のディスプレイモードへ戻せませんでした。", $"ChangeDisplaySettingsEx={status}");
    }

    internal sealed class DisplayState
    {
        public string DeviceName { get; set; } = "";
        public uint BitsPerPel { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint DisplayFlags { get; set; }
        public uint RefreshRate { get; set; }
        public uint Orientation { get; set; }

        public static DisplayState From(string deviceName, DevMode mode) => new()
        {
            DeviceName = deviceName,
            BitsPerPel = mode.BitsPerPel,
            Width = mode.PelsWidth,
            Height = mode.PelsHeight,
            DisplayFlags = mode.DisplayFlags,
            RefreshRate = mode.DisplayFrequency,
            Orientation = mode.DisplayOrientation
        };

        public DevMode ToDevMode()
        {
            var mode = DevMode.Create();
            mode.Fields = DmBitsPerPel | DmPelsWidth | DmPelsHeight |
                DmDisplayFlags | DmDisplayFrequency | DmDisplayOrientation;
            mode.BitsPerPel = BitsPerPel;
            mode.PelsWidth = Width;
            mode.PelsHeight = Height;
            mode.DisplayFlags = DisplayFlags;
            mode.DisplayFrequency = RefreshRate;
            mode.DisplayOrientation = Orientation;
            return mode;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        public static DisplayDevice Create() => new() { Size = Marshal.SizeOf<DisplayDevice>() };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2;
        public uint PanningWidth, PanningHeight;

        public static DevMode Create() => new()
        {
            DeviceName = "",
            FormName = "",
            Size = (ushort)Marshal.SizeOf<DevMode>()
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint deviceNumber, ref DisplayDevice displayDevice, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string deviceName, uint modeNumber, ref DevMode devMode, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DevMode devMode, IntPtr hwnd, uint flags, IntPtr param);
}

public sealed class DisplayModeActionHandler : IModeActionHandler
{
    private readonly DisplayModeService _service;
    public string Id => "display-refresh-rate";
    public string DisplayName => "モニターのリフレッシュレート";

    public DisplayModeActionHandler(DisplayModeService? service = null) =>
        _service = service ?? new DisplayModeService();

    public Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        var config = context.Mode.Display;
        if (string.IsNullOrWhiteSpace(config.DeviceName) || config.RefreshRate is null)
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UserSkipped, "変更しない設定です。"));
        var displays = _service.GetDisplays();
        var display = displays.Value?.FirstOrDefault(value =>
            string.Equals(value.DeviceName, config.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (display is null)
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.TargetNotFoundSkipped, "対象モニターが接続されていません。", displays.TechnicalDetails));
        if (!display.SupportedRefreshRates.Contains(config.RefreshRate.Value))
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UnsupportedSkipped, "指定したリフレッシュレートを現在の構成で利用できません。"));
        if (!config.IsTrusted || !string.Equals(config.HardwareSignature, display.HardwareSignature, StringComparison.Ordinal))
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UserSkipped, "現在のモニター構成では未確認の設定です。編集画面でテストしてください。"));
        var test = _service.ApplyRefreshRate(config.DeviceName, config.RefreshRate.Value, testOnly: true);
        return Task.FromResult(test.IsSuccess ? ActionPreflightResult.Ready() : ActionPreflightResult.Skip(ActionExecutionStatus.UnsupportedSkipped, test.UserMessage, test.TechnicalDetails));
    }

    public Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        var result = _service.GetCurrentMode(context.Mode.Display.DeviceName!);
        return Task.FromResult(result.IsSuccess && result.Value is not null
            ? ActionCaptureResult.Success(result.Value)
            : ActionCaptureResult.Skip(ActionExecutionStatus.ApplyFailed, "変更前のディスプレイモードを記録できないため変更しません。", result.TechnicalDetails));
    }

    public Task<ActionExecutionResult> ApplyAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var result = _service.ApplyRefreshRate(context.Mode.Display.DeviceName!, context.Mode.Display.RefreshRate!.Value);
        if (result.IsSuccess)
        {
            var verify = _service.GetCurrentMode(context.Mode.Display.DeviceName!);
            if (!verify.IsSuccess || verify.Value?.RefreshRate != context.Mode.Display.RefreshRate)
                result = OperationResult.Failure("リフレッシュレートの変更を確認できませんでした。", verify.TechnicalDetails);
        }
        return Task.FromResult(ActionResults.Create(this, result.IsSuccess ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.ApplyFailed, result.UserMessage, result.TechnicalDetails));
    }

    public Task<ActionExecutionResult> RestoreAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var state = snapshot.OriginalState.Deserialize<DisplayModeService.DisplayState>();
        if (state is null)
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.RestoreFailed, "元に戻すための情報がありません。"));
        var result = _service.Restore(state);
        return Task.FromResult(ActionResults.Create(this, result.IsSuccess ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed, result.UserMessage, result.TechnicalDetails));
    }
}
