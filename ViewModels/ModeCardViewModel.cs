using PCModeSwitcher.Models;
using PCModeSwitcher.Services;

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
        FormatSummary(
            HasStructuredPower && _mode.Power.DisplayTimeoutAcSeconds is null ? null : _mode.DisplayTimeoutAc,
            HasStructuredPower && _mode.Power.DisplayTimeoutDcSeconds is null ? null : _mode.DisplayTimeoutBattery);
    public string SleepSummary =>
        FormatSummary(
            HasStructuredPower && _mode.Power.SleepTimeoutAcSeconds is null ? null : _mode.SleepTimeoutAc,
            HasStructuredPower && _mode.Power.SleepTimeoutDcSeconds is null ? null : _mode.SleepTimeoutBattery);
    public string PowerPlanName => _mode.Power.ChangePowerPlan
        ? _powerPlanName(_mode.PowerPlanId)
        : LocalizationService.Get("Common.NoChange");
    public bool ShowMicrophoneControls
    {
        get => _showMicrophoneControls;
        set
        {
            if (SetProperty(ref _showMicrophoneControls, value))
                OnPropertyChanged(nameof(TrayToolTipText));
        }
    }
    public string MicrophoneSummary =>
        _mode.Audio.Microphone.VolumePercent is null
            ? EffectiveMicrophoneMute switch
            {
                AudioMuteSetting.NoChange => LocalizationService.Get("Common.NoChange"),
                AudioMuteSetting.Mute => "OFF",
                AudioMuteSetting.Unmute => "ON",
                _ => LocalizationService.Translate("設定エラー")
            }
            : LocalizationService.Format("Card.VolumeMute", _mode.Audio.Microphone.VolumePercent, _mode.Audio.Microphone.Mute switch
            {
                AudioMuteSetting.Mute => "OFF",
                AudioMuteSetting.Unmute => "ON",
                _ => LocalizationService.Translate("ミュート変更なし")
            });
    private AudioMuteSetting EffectiveMicrophoneMute =>
        _mode.Audio.Microphone.Mute != AudioMuteSetting.NoChange
            ? _mode.Audio.Microphone.Mute
            : _mode.MicrophoneMute switch
            {
                MicrophoneMuteSetting.Mute => AudioMuteSetting.Mute,
                MicrophoneMuteSetting.Unmute => AudioMuteSetting.Unmute,
                _ => AudioMuteSetting.NoChange
            };
    private bool HasStructuredPower =>
        _mode.Power.PowerPlanId != Guid.Empty || _mode.PowerPlanId == Guid.Empty;
    public string TrayToolTipText => string.Join(
        Environment.NewLine,
        ShowMicrophoneControls
            ?
            [
                LocalizationService.Format("Card.TrayDisplay", DisplaySummary),
                LocalizationService.Format("Card.TraySleep", SleepSummary),
                LocalizationService.Format("Card.TrayPower", PowerPlanName),
                LocalizationService.Format("Card.TrayMicrophone", MicrophoneSummary)
            ]
            :
            [
                LocalizationService.Format("Card.TrayDisplay", DisplaySummary),
                LocalizationService.Format("Card.TraySleep", SleepSummary),
                LocalizationService.Format("Card.TrayPower", PowerPlanName)
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

    private string FormatSummary(uint? acSeconds, uint? batterySeconds) => _hasBattery
        ? LocalizationService.Format("Card.AcBattery", FormatTimeout(acSeconds), FormatTimeout(batterySeconds))
        : LocalizationService.Format("Card.AcOnly", FormatTimeout(acSeconds));

    public static string FormatTimeout(uint? seconds) =>
        seconds is null ? LocalizationService.Get("Common.NoChange") : FormatTimeout(seconds.Value);

    public static string FormatTimeout(uint seconds) => seconds switch
    {
        0 => LocalizationService.Get("Common.None"),
        3600 => LocalizationService.Get("Choice.OneHour"),
        7200 => LocalizationService.Get("Choice.TwoHours"),
        _ when seconds % 60 == 0 => LocalizationService.Format("Card.Minutes", seconds / 60),
        _ => LocalizationService.Format("Card.Seconds", seconds)
    };
}
