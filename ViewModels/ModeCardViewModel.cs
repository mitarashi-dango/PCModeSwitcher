using PCModeSwitcher.Models;

namespace PCModeSwitcher.ViewModels;

public sealed class ModeCardViewModel : ObservableObject
{
    private readonly Func<Guid, string> _powerPlanName;
    private readonly bool _hasBattery;
    private PcMode _mode;

    public ModeCardViewModel(PcMode mode, Func<Guid, string> powerPlanName, bool hasBattery)
    {
        _mode = mode;
        _powerPlanName = powerPlanName;
        _hasBattery = hasBattery;
    }

    public PcMode Mode => _mode;
    public bool HasBattery => _hasBattery;
    public string Name => _mode.Name;
    public string Icon => _mode.Icon;
    public string DisplaySummary =>
        FormatSummary(_mode.DisplayTimeoutAc, _mode.DisplayTimeoutBattery);
    public string SleepSummary =>
        FormatSummary(_mode.SleepTimeoutAc, _mode.SleepTimeoutBattery);
    public string PowerPlanName => _powerPlanName(_mode.PowerPlanId);

    public void Replace(PcMode mode)
    {
        _mode = mode;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(SleepSummary));
        OnPropertyChanged(nameof(PowerPlanName));
    }

    public void RefreshPlanName() => OnPropertyChanged(nameof(PowerPlanName));

    private string FormatSummary(uint acSeconds, uint batterySeconds) => _hasBattery
        ? $"AC {FormatTimeout(acSeconds)} / バッテリー {FormatTimeout(batterySeconds)}"
        : $"AC {FormatTimeout(acSeconds)}";

    public static string FormatTimeout(uint seconds) => seconds switch
    {
        0 => "なし",
        3600 => "1時間",
        7200 => "2時間",
        _ when seconds % 60 == 0 => $"{seconds / 60}分",
        _ => $"{seconds}秒"
    };
}
