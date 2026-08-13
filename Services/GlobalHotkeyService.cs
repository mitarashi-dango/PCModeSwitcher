using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModifierNoRepeat = 0x4000;
    private const int GameHotkeyId = 0x5101;
    private const int WorkHotkeyId = 0x5102;
    private const int NormalHotkeyId = 0x5103;
    private const int Custom1HotkeyId = 0x5104;
    private const int Custom2HotkeyId = 0x5105;
    private const int Custom3HotkeyId = 0x5106;
    private const int Custom4HotkeyId = 0x5107;
    private const int Custom5HotkeyId = 0x5108;
    private const int Custom6HotkeyId = 0x5109;

    private readonly Dictionary<int, ModeHotkey> _registrations = [];
    private Window? _window;
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _disposed;

    public event EventHandler<ModeHotkeyPressedEventArgs>? HotkeyPressed;

    public void Attach(Window window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(window);
        if (_window is not null)
        {
            throw new InvalidOperationException("グローバルショートカットは一度だけ初期化できます。");
        }

        _window = window;
        _window.SourceInitialized += OnSourceInitialized;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            InitializeWindowSource();
        }
    }

    public OperationResult ReplaceBindings(IReadOnlyCollection<ModeHotkey> hotkeys)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var validation = HotkeyValidator.Validate(hotkeys);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (_windowHandle == IntPtr.Zero)
        {
            return OperationResult.Failure("ショートカットを登録するための画面を初期化できませんでした。");
        }

        var previousBindings = _registrations.Values.Select(hotkey => hotkey.Copy()).ToList();
        UnregisterAll();
        var result = RegisterAll(hotkeys);
        if (result.IsSuccess)
        {
            return result;
        }

        UnregisterAll();
        var rollback = RegisterAll(previousBindings);
        return rollback.IsSuccess
            ? result
            : OperationResult.Failure(
                $"{result.UserMessage} 以前のショートカットも復元できませんでした。",
                $"登録: {result.TechnicalDetails}{Environment.NewLine}復元: {rollback.TechnicalDetails}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        if (_source is not null)
        {
            _source.RemoveHook(WindowMessageHook);
            _source = null;
        }

        if (_window is not null)
        {
            _window.SourceInitialized -= OnSourceInitialized;
            _window = null;
        }

        _windowHandle = IntPtr.Zero;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => InitializeWindowSource();

    private void InitializeWindowSource()
    {
        if (_window is null || _source is not null)
        {
            return;
        }

        _windowHandle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowMessageHook);
    }

    private OperationResult RegisterAll(IEnumerable<ModeHotkey> hotkeys)
    {
        foreach (var hotkey in hotkeys.Where(hotkey => hotkey.IsConfigured))
        {
            var id = GetRegistrationId(hotkey.ModeId);
            var modifiers = (uint)hotkey.Modifiers | ModifierNoRepeat;
            if (!RegisterHotKey(_windowHandle, id, modifiers, (uint)hotkey.VirtualKey))
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                return OperationResult.Failure(
                    $"{HotkeyValidator.GetModeName(hotkey.ModeId)}のショートカット「{HotkeyValidator.Format(hotkey)}」を登録できませんでした。他のアプリやWindowsで使用されていない組み合わせを選んでください。",
                    error.Message);
            }

            _registrations[id] = hotkey.Copy();
        }

        return OperationResult.Success();
    }

    private void UnregisterAll()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            foreach (var id in _registrations.Keys)
            {
                UnregisterHotKey(_windowHandle, id);
            }
        }

        _registrations.Clear();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey && _registrations.TryGetValue(wParam.ToInt32(), out var hotkey))
        {
            handled = true;
            HotkeyPressed?.Invoke(this, new ModeHotkeyPressedEventArgs(hotkey.ModeId));
        }

        return IntPtr.Zero;
    }

    private static int GetRegistrationId(string modeId) => modeId.ToLowerInvariant() switch
    {
        "game" => GameHotkeyId,
        "work" => WorkHotkeyId,
        "normal" => NormalHotkeyId,
        "custom1" => Custom1HotkeyId,
        "custom2" => Custom2HotkeyId,
        "custom3" => Custom3HotkeyId,
        "custom4" => Custom4HotkeyId,
        "custom5" => Custom5HotkeyId,
        "custom6" => Custom6HotkeyId,
        _ => throw new ArgumentOutOfRangeException(nameof(modeId), modeId, "未対応のモードです。")
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
