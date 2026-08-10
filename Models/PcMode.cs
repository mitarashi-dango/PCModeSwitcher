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

    public PcMode Copy() => new()
    {
        Id = Id,
        Name = Name,
        Icon = Icon,
        DisplayTimeoutAc = DisplayTimeoutAc,
        DisplayTimeoutBattery = DisplayTimeoutBattery,
        SleepTimeoutAc = SleepTimeoutAc,
        SleepTimeoutBattery = SleepTimeoutBattery,
        PowerPlanId = PowerPlanId
    };
}
