using System.Runtime.InteropServices;
using System.Text.Json;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

internal enum AudioDataFlow { Render, Capture }

internal sealed class AudioEndpointState
{
    public string EndpointId { get; set; } = "";
    public float VolumeScalar { get; set; }
    public bool Muted { get; set; }
}

internal sealed class AudioEndpointService
{
    private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public OperationResult<AudioEndpointState> GetDefaultState(AudioDataFlow flow) =>
        WithEndpoint(flow, null, (device, endpoint) =>
        {
            ThrowIfFailed(device.GetId(out var id));
            ThrowIfFailed(endpoint.GetMasterVolumeLevelScalar(out var volume));
            ThrowIfFailed(endpoint.GetMute(out var muted));
            return OperationResult<AudioEndpointState>.Success(new AudioEndpointState
            {
                EndpointId = id,
                VolumeScalar = volume,
                Muted = muted
            });
        });

    public OperationResult ApplyDefault(AudioDataFlow flow, AudioEndpointConfiguration configuration) =>
        WithEndpoint(flow, null, (_, endpoint) => Apply(endpoint, configuration));

    public OperationResult Restore(AudioDataFlow flow, AudioEndpointState state) =>
        WithEndpoint(flow, state.EndpointId, (_, endpoint) =>
        {
            var context = Guid.Empty;
            ThrowIfFailed(endpoint.SetMasterVolumeLevelScalar(
                Math.Clamp(state.VolumeScalar, 0f, 1f), ref context));
            ThrowIfFailed(endpoint.SetMute(state.Muted, ref context));
            return OperationResult.Success("音量とミュート状態を戻しました。");
        });

    private static OperationResult Apply(
        IAudioEndpointVolume endpoint,
        AudioEndpointConfiguration configuration)
    {
        var context = Guid.Empty;
        if (configuration.VolumePercent is not null)
        {
            if (configuration.VolumePercent is < 0 or > 100)
                return OperationResult.Failure("音量は0～100%で指定してください。");
            ThrowIfFailed(endpoint.SetMasterVolumeLevelScalar(
                configuration.VolumePercent.Value / 100f, ref context));
        }
        if (configuration.Mute != AudioMuteSetting.NoChange)
            ThrowIfFailed(endpoint.SetMute(configuration.Mute == AudioMuteSetting.Mute, ref context));
        return OperationResult.Success("音量設定を変更しました。");
    }

    private static OperationResult<T> WithEndpoint<T>(
        AudioDataFlow flow,
        string? endpointId,
        Func<IMMDevice, IAudioEndpointVolume, OperationResult<T>> action)
    {
        object? enumeratorObject = null;
        object? deviceObject = null;
        object? endpointObject = null;
        try
        {
            var type = Type.GetTypeFromCLSID(EnumeratorClsid, true)
                ?? throw new COMException("MMDeviceEnumeratorを作成できませんでした。");
            enumeratorObject = Activator.CreateInstance(type)
                ?? throw new COMException("MMDeviceEnumeratorを作成できませんでした。");
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            IMMDevice device;
            if (string.IsNullOrWhiteSpace(endpointId))
                ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out device));
            else
                ThrowIfFailed(enumerator.GetDevice(endpointId, out device));
            deviceObject = device;
            var iid = typeof(IAudioEndpointVolume).GUID;
            ThrowIfFailed(device.Activate(ref iid, ClsCtx.All, IntPtr.Zero, out endpointObject));
            return action(device, (IAudioEndpointVolume)endpointObject);
        }
        catch (Exception ex)
        {
            return OperationResult<T>.Failure("スピーカーまたはマイクを操作できませんでした。", ex.ToString());
        }
        finally
        {
            Release(endpointObject);
            Release(deviceObject);
            Release(enumeratorObject);
        }
    }

    private static OperationResult WithEndpoint(
        AudioDataFlow flow,
        string? endpointId,
        Func<IMMDevice, IAudioEndpointVolume, OperationResult> action)
    {
        var result = WithEndpoint(flow, endpointId, (device, endpoint) =>
        {
            var value = action(device, endpoint);
            return value.IsSuccess
                ? OperationResult<bool>.Success(true, value.UserMessage)
                : OperationResult<bool>.Failure(value.UserMessage, value.TechnicalDetails);
        });
        return result.IsSuccess
            ? OperationResult.Success(result.UserMessage)
            : OperationResult.Failure(result.UserMessage, result.TechnicalDetails);
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    private static void Release(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch (InvalidComObjectException) { }
    }

    private enum ERole { Console, Multimedia, Communications }
    [Flags]
    private enum ClsCtx : uint
    {
        InprocServer = 1, InprocHandler = 2, LocalServer = 4, RemoteServer = 16,
        All = InprocServer | InprocHandler | LocalServer | RemoteServer
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(AudioDataFlow flow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(AudioDataFlow flow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, ClsCtx context, IntPtr parameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint accessMode, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}

public sealed class AudioActionHandler : IModeActionHandler
{
    private readonly AudioEndpointService _service;
    public string Id => "audio";
    public string DisplayName => "音量とミュート";

    public AudioActionHandler() : this(new AudioEndpointService()) { }
    internal AudioActionHandler(AudioEndpointService service) => _service = service;

    public Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        var output = context.Mode.Audio.Output;
        var microphone = context.Mode.Audio.Microphone;
        if (!Changes(output) && !Changes(microphone))
            return Task.FromResult(ActionPreflightResult.Skip(ActionExecutionStatus.UserSkipped, "変更しない設定です。"));
        if (!Valid(output) || !Valid(microphone))
            return Task.FromResult(ActionPreflightResult.Fatal("音量は0～100%で指定してください。"));
        return Task.FromResult(ActionPreflightResult.Ready());
    }

    public Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken)
    {
        AudioEndpointState? output = null;
        AudioEndpointState? microphone = null;
        if (Changes(context.Mode.Audio.Output))
        {
            var result = _service.GetDefaultState(AudioDataFlow.Render);
            if (!result.IsSuccess)
                return Task.FromResult(ActionCaptureResult.Skip(ActionExecutionStatus.TargetNotFoundSkipped, "既定の音声出力を記録できないため音声設定を変更しません。", result.TechnicalDetails));
            output = result.Value;
        }
        if (Changes(context.Mode.Audio.Microphone))
        {
            var result = _service.GetDefaultState(AudioDataFlow.Capture);
            if (!result.IsSuccess)
                return Task.FromResult(ActionCaptureResult.Skip(ActionExecutionStatus.TargetNotFoundSkipped, "既定のマイクを記録できないため音声設定を変更しません。", result.TechnicalDetails));
            microphone = result.Value;
        }
        return Task.FromResult(ActionCaptureResult.Success(new AudioSnapshot { Output = output, Microphone = microphone }));
    }

    public Task<ActionExecutionResult> ApplyAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var results = new List<OperationResult>();
        if (Changes(context.Mode.Audio.Output))
            results.Add(_service.ApplyDefault(AudioDataFlow.Render, context.Mode.Audio.Output));
        if (Changes(context.Mode.Audio.Microphone))
            results.Add(_service.ApplyDefault(AudioDataFlow.Capture, context.Mode.Audio.Microphone));
        return Task.FromResult(Combine(results, false));
    }

    public Task<ActionExecutionResult> RestoreAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var state = snapshot.OriginalState.Deserialize<AudioSnapshot>();
        if (state is null)
            return Task.FromResult(ActionResults.Create(this, ActionExecutionStatus.RestoreFailed, "元に戻すための情報がありません。"));
        var results = new List<OperationResult>();
        if (state.Output is not null) results.Add(_service.Restore(AudioDataFlow.Render, state.Output));
        if (state.Microphone is not null) results.Add(_service.Restore(AudioDataFlow.Capture, state.Microphone));
        return Task.FromResult(Combine(results, true));
    }

    private ActionExecutionResult Combine(IReadOnlyCollection<OperationResult> results, bool restore)
    {
        var failed = results.Where(result => !result.IsSuccess).ToList();
        var success = failed.Count == 0;
        return ActionResults.Create(this,
            restore
                ? success ? ActionExecutionStatus.RestoreSucceeded : ActionExecutionStatus.RestoreFailed
                : success ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.ApplyFailed,
            success
                ? restore ? "音量とミュートを戻しました。" : "音量とミュートを変更しました。"
                : restore ? "一部の音声設定を元に戻せませんでした。" : "一部の音声設定を変更できませんでした。",
            success ? null : string.Join("; ", failed.Select(result => result.TechnicalDetails ?? result.UserMessage)));
    }

    private static bool Changes(AudioEndpointConfiguration value) =>
        value.VolumePercent is not null || value.Mute != AudioMuteSetting.NoChange;
    private static bool Valid(AudioEndpointConfiguration value) =>
        value.VolumePercent is null or >= 0 and <= 100;

    private sealed class AudioSnapshot
    {
        public AudioEndpointState? Output { get; set; }
        public AudioEndpointState? Microphone { get; set; }
    }
}
