namespace PCModeSwitcher.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public List<PcMode> Modes { get; set; } = [];
    public string? LastAppliedModeId { get; set; }
    public CloseButtonBehavior CloseButtonBehavior { get; set; } = CloseButtonBehavior.MinimizeToTray;
    public bool ShowTrayNotification { get; set; }
}
