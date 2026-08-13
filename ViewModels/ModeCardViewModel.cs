using PCModeSwitcher.Models;

namespace PCModeSwitcher.ViewModels;

public sealed class ModeCardViewModel : ObservableObject
{
    private readonly Func<Guid, string> _powerPlanName;
    private readonly bool _hasBattery;
    private PcMode _mode;
    private bool _showMicrophoneControls;

    public ModeCardViewModel(
        PcMode mode,
        Func<Guid, string> powerPlanName,
        bool hasBattery,
        bool showMicrophoneControls = true)
    {
        _mode = mode;
        _powerPlanName = powerPlanName;
        _hasBattery = hasBattery;
        _showMicrophoneControls = showMicrophoneControls;
    }

    public PcMode Mode => _mode;
    public bool HasBattery => _hasBattery;
    public string Name => _mode.Name;
    public string Icon => _mode.Icon;
    public bool HasCustomIcon => ModeIconAssets.HasCustomIcon(_mode.Id);
    public string? CustomIconSource => ModeIconAssets.GetCustomIconSource(_mode.Id);
    public string DisplaySummary =>
        FormatSummary(_mode.DisplayTimeoutAc, _mode.DisplayTimeoutBattery);
    public string SleepSummary =>
        FormatSummary(_mode.SleepTimeoutAc, _mode.SleepTimeoutBattery);
    public string PowerPlanName => _powerPlanName(_mode.PowerPlanId);
    public bool ShowMicrophoneControls
    {
        get => _showMicrophoneControls;
        set
        {
            if (SetProperty(ref _showMicrophoneControls, value))
                OnPropertyChanged(nameof(TrayToolTipText));
        }
    }
    public string MicrophoneSummary => _mode.MicrophoneMute switch
    {
        MicrophoneMuteSetting.NoChange => "変更しない",
        MicrophoneMuteSetting.Mute => "OFF",
        MicrophoneMuteSetting.Unmute => "ON",
        _ => "設定エラー"
    };
    public string TrayToolTipText => string.Join(
        Environment.NewLine,
        ShowMicrophoneControls
            ?
            [
                $"画面OFF: {DisplaySummary}",
                $"スリープ: {SleepSummary}",
                $"電源モード: {PowerPlanName}",
                $"マイク設定（適用時）: {MicrophoneSummary}"
            ]
            :
            [
                $"画面OFF: {DisplaySummary}",
                $"スリープ: {SleepSummary}",
                $"電源モード: {PowerPlanName}"
            ]);

    public void Replace(PcMode mode)
    {
        _mode = mode;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasCustomIcon));
        OnPropertyChanged(nameof(CustomIconSource));
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(SleepSummary));
        OnPropertyChanged(nameof(PowerPlanName));
        OnPropertyChanged(nameof(MicrophoneSummary));
        OnPropertyChanged(nameof(TrayToolTipText));
    }

    public void RefreshPlanName()
    {
        OnPropertyChanged(nameof(PowerPlanName));
        OnPropertyChanged(nameof(TrayToolTipText));
    }

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
