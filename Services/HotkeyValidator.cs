using System.Windows.Input;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public static class HotkeyValidator
{
    private const HotkeyModifiers AllowedModifiers =
        HotkeyModifiers.Alt |
        HotkeyModifiers.Control |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Windows;

    private static readonly string[] ModeIds = ["game", "work", "normal", "custom"];

    public static OperationResult Validate(IReadOnlyCollection<ModeHotkey> hotkeys)
    {
        if (hotkeys.Count != ModeIds.Length ||
            hotkeys.Select(hotkey => hotkey.ModeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ModeIds.Length ||
            ModeIds.Any(modeId => !hotkeys.Any(hotkey =>
                string.Equals(hotkey.ModeId, modeId, StringComparison.OrdinalIgnoreCase))))
        {
            return OperationResult.Failure("ショートカットのモード構成が正しくありません。");
        }

        foreach (var hotkey in hotkeys)
        {
            var hasUnsupportedModifier = (hotkey.Modifiers & ~AllowedModifiers) != 0;
            var isEmpty = hotkey.Modifiers == HotkeyModifiers.None && hotkey.VirtualKey == 0;
            var isComplete = hotkey.Modifiers != HotkeyModifiers.None &&
                hotkey.VirtualKey is > 0 and <= 0xFE;

            if (hasUnsupportedModifier || (!isEmpty && !isComplete))
            {
                return OperationResult.Failure(
                    $"{GetModeName(hotkey.ModeId)}のショートカットが正しくありません。");
            }

            // F12はWindowsのデバッガー用に常時予約されているため登録できない。
            if (hotkey.VirtualKey == 0x7B)
            {
                return OperationResult.Failure("F12はWindowsで予約されているため使用できません。");
            }
        }

        var duplicate = hotkeys
            .Where(hotkey => hotkey.IsConfigured)
            .GroupBy(hotkey => (hotkey.Modifiers, hotkey.VirtualKey))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return OperationResult.Failure(
                $"同じショートカット「{Format(duplicate.First())}」を複数のモードには設定できません。");
        }

        return OperationResult.Success();
    }

    public static string Format(ModeHotkey hotkey)
    {
        if (!hotkey.IsConfigured)
        {
            return "未設定";
        }

        var parts = new List<string>();
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control))
            parts.Add("Ctrl");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt))
            parts.Add("Alt");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift))
            parts.Add("Shift");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Windows))
            parts.Add("Win");

        parts.Add(FormatKey(KeyInterop.KeyFromVirtualKey(hotkey.VirtualKey)));
        return string.Join(" + ", parts);
    }

    public static string GetModeName(string modeId) => modeId.ToLowerInvariant() switch
    {
        "game" => "GAME",
        "work" => "WORK",
        "normal" => "NORMAL",
        "custom" => "CUSTOM",
        _ => modeId
    };

    private static string FormatKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {((int)key - (int)Key.NumPad0)}",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString()
    };
}
