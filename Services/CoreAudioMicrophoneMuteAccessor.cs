using System.Runtime.InteropServices;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

internal sealed class CoreAudioMicrophoneMuteAccessor : IMicrophoneMuteAccessor
{
    private static readonly Guid MMDeviceEnumeratorClsid =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public OperationResult<bool> GetMuted()
    {
        object? enumeratorObject = null;
        object? deviceObject = null;
        object? endpointObject = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid, throwOnError: true)
                ?? throw new COMException("MMDeviceEnumeratorを作成できませんでした。");
            enumeratorObject = Activator.CreateInstance(enumeratorType)
                ?? throw new COMException("MMDeviceEnumeratorを作成できませんでした。");
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;

            ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(
                EDataFlow.Capture, ERole.Console, out var device));
            deviceObject = device;

            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            ThrowIfFailed(device.Activate(
                ref interfaceId, ClsCtx.All, IntPtr.Zero, out endpointObject));
            var endpoint = (IAudioEndpointVolume)endpointObject;

            ThrowIfFailed(endpoint.GetMute(out var muted));
            return OperationResult<bool>.Success(muted);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                "既定のマイクを読み取れませんでした。",
                ex.Message);
        }
        finally
        {
            ReleaseComObject(endpointObject);
            ReleaseComObject(deviceObject);
            ReleaseComObject(enumeratorObject);
        }
    }

    public OperationResult SetMuted(bool muted)
    {
        object? enumeratorObject = null;
        object? deviceObject = null;
        object? endpointObject = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid, throwOnError: true)
                ?? throw new COMException("MMDeviceEnumeratorを作成できませんでした。");
            enumeratorObject = Activator.CreateInstance(enumeratorType)
                ?? throw new COMException("MMDeviceEnumeratorを作成できませんでした。");
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;

            ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(
                EDataFlow.Capture, ERole.Console, out var device));
            deviceObject = device;

            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            ThrowIfFailed(device.Activate(
                ref interfaceId, ClsCtx.All, IntPtr.Zero, out endpointObject));
            var endpoint = (IAudioEndpointVolume)endpointObject;

            var eventContext = Guid.Empty;
            ThrowIfFailed(endpoint.SetMute(muted, ref eventContext));
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(
                "既定のマイクを変更できませんでした。",
                ex.Message);
        }
        finally
        {
            ReleaseComObject(endpointObject);
            ReleaseComObject(deviceObject);
            ReleaseComObject(enumeratorObject);
        }
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
            Marshal.ThrowExceptionForHR(hresult);
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch (InvalidComObjectException)
        {
            // すでに解放済みなら追加の処理は不要。
        }
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [Flags]
    private enum ClsCtx : uint
    {
        InprocServer = 0x1,
        InprocHandler = 0x2,
        LocalServer = 0x4,
        RemoteServer = 0x10,
        All = InprocServer | InprocHandler | LocalServer | RemoteServer
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid interfaceId,
            ClsCtx classContext,
            IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfaceObject);

        [PreserveSig]
        int OpenPropertyStore(uint accessMode, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channelNumber, float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channelNumber, out float levelDb);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channelNumber, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
