using PCModeSwitcher.Models;

namespace PCModeSwitcher.ViewModels;

public sealed class ModeCardViewModel : ObservableObject
{
    private readonly Func<Guid, string> _powerPlanName;
    private PcMode _mode;

    public ModeCardViewModel(PcMode mode, Func<Guid, string> powerPlanName)
    {
        _mode = mode;
        _powerPlanName = powerPlanName;
    }

    public PcMode Mode => _mode;
    public string Name => _mode.Name;
    public string Icon => _mode.Icon;
    public string DisplaySummary =>
        $"AC {FormatTimeout(_mode.DisplayTimeoutAc)} / バッテリー {FormatTimeout(_mode.DisplayTimeoutBattery)}";
    public string SleepSummary =>
        $"AC {FormatTimeout(_mode.SleepTimeoutAc)} / バッテリー {FormatTimeout(_mode.SleepTimeoutBattery)}";
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

    public static string FormatTimeout(uint seconds) => seconds switch
    {
        0 => "なし",
        3600 => "1時間",
        7200 => "2時間",
        _ when seconds % 60 == 0 => $"{seconds / 60}分",
        _ => $"{seconds}秒"
    };
}
