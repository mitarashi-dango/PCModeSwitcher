namespace PCModeSwitcher.Models;

public sealed class AppSettings
{
    [System.Text.Json.Serialization.JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }
    public int Version { get; set; } = 1;
    public List<PcMode> Modes { get; set; } = [];
    public string? LastAppliedModeId { get; set; }
    public CloseButtonBehavior CloseButtonBehavior { get; set; } = CloseButtonBehavior.MinimizeToTray;
    public bool ShowTrayNotification { get; set; }
    public bool StartWithWindows { get; set; }
    public bool ShowMicrophoneControls { get; set; } = true;
    public string Language { get; set; } = Services.AppLanguages.System;
    public List<ModeHotkey> Hotkeys { get; set; } = [];
    public List<string> VisibleModeIds { get; set; } = [];
    public ModeHotkey RestoreHotkey { get; set; } = new() { ModeId = "restore" };
}
