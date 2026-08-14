namespace PCModeSwitcher.Models;

public sealed class PcMode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "●";
    public uint DisplayTimeoutAc { get; set; }
    public uint DisplayTimeoutBattery { get; set; }
    public uint SleepTimeoutAc { get; set; }
    public uint SleepTimeoutBattery { get; set; }
    public Guid PowerPlanId { get; set; }
    public MicrophoneMuteSetting MicrophoneMute { get; set; } = MicrophoneMuteSetting.NoChange;
    public bool IsEnabled { get; set; } = true;
    public PowerConfiguration Power { get; set; } = new();
    public DisplayConfiguration Display { get; set; } = new();
    public AudioConfiguration Audio { get; set; } = new();
    public List<LaunchItem> LaunchItems { get; set; } = [];
    public List<CloseProcessRule> CloseProcessRules { get; set; } = [];
    public List<ProcessMonitorRule> MonitorRules { get; set; } = [];
    public List<WindowPlacementRule> WindowPlacements { get; set; } = [];

    public PcMode Copy() => new()
    {
        Id = Id,
        Name = Name,
        Icon = Icon,
        DisplayTimeoutAc = DisplayTimeoutAc,
        DisplayTimeoutBattery = DisplayTimeoutBattery,
        SleepTimeoutAc = SleepTimeoutAc,
        SleepTimeoutBattery = SleepTimeoutBattery,
        PowerPlanId = PowerPlanId,
        MicrophoneMute = MicrophoneMute,
        IsEnabled = IsEnabled,
        Power = Power.Copy(),
        Display = Display.Copy(),
        Audio = Audio.Copy(),
        LaunchItems = LaunchItems.Select(item => item.Copy()).ToList(),
        CloseProcessRules = CloseProcessRules.Select(rule => rule.Copy()).ToList(),
        MonitorRules = MonitorRules.Select(rule => rule.Copy()).ToList(),
        WindowPlacements = WindowPlacements.Select(rule => rule.Copy()).ToList()
    };
}
