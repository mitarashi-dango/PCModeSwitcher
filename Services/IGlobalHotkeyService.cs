using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class ModeHotkeyPressedEventArgs(string modeId) : EventArgs
{
    public string ModeId { get; } = modeId;
}
public interface IGlobalHotkeyService
{
    event EventHandler<ModeHotkeyPressedEventArgs>? HotkeyPressed;
    OperationResult ReplaceBindings(IReadOnlyCollection<ModeHotkey> hotkeys);
}
