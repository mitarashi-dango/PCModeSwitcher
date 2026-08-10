using System.Text.Json.Serialization;

namespace PCModeSwitcher.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}
public sealed class ModeHotkey
{
    public string ModeId { get; set; } = "";
    public HotkeyModifiers Modifiers { get; set; }
    public int VirtualKey { get; set; }

    [JsonIgnore]
    public bool IsConfigured => Modifiers != HotkeyModifiers.None && VirtualKey > 0;

    public ModeHotkey Copy() => new()
    {
        ModeId = ModeId,
        Modifiers = Modifiers,
        VirtualKey = VirtualKey
    };
}
