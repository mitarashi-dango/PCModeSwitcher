using System.Collections.ObjectModel;
using System.Windows;
using PCModeSwitcher.Models;
using PCModeSwitcher.Services;

namespace PCModeSwitcher.Views;

public partial class ModeEditorWindow : Window
{
    private readonly PcMode _source;

    public string ModeName => _source.Name;
    public bool HasBattery { get; }
    public bool ShowMicrophoneControls { get; }
    public ObservableCollection<TimeoutChoice> DisplayChoices { get; } = CreateChoices([1, 2, 5, 10, 15, 30, 60, 0]);
    public ObservableCollection<TimeoutChoice> SleepChoices { get; } = CreateChoices([1, 5, 10, 15, 30, 60, 120, 0]);
    public ObservableCollection<PowerPlan> PowerPlans { get; }
    public ObservableCollection<MicrophoneMuteChoice> MicrophoneChoices { get; } =
    [
        new(MicrophoneMuteSetting.NoChange, LocalizationService.Get("Common.NoChange")),
        new(MicrophoneMuteSetting.Mute, "OFF"),
        new(MicrophoneMuteSetting.Unmute, "ON")
    ];
    public TimeoutChoice? DisplayAc { get; set; }
    public TimeoutChoice? DisplayBattery { get; set; }
    public TimeoutChoice? SleepAc { get; set; }
    public TimeoutChoice? SleepBattery { get; set; }
    public PowerPlan? SelectedPowerPlan { get; set; }
    public MicrophoneMuteChoice? SelectedMicrophoneMute { get; set; }
    public PcMode? EditedMode { get; private set; }

    public ModeEditorWindow(
        PcMode mode,
        IReadOnlyList<PowerPlan> plans,
        bool hasBattery,
        bool showMicrophoneControls,
        Window owner)
    {
        InitializeComponent();
        Owner = owner;
        _source = mode;
        HasBattery = hasBattery;
        ShowMicrophoneControls = showMicrophoneControls;
        PowerPlans = new ObservableCollection<PowerPlan>(plans);
        DisplayAc = FindOrAdd(DisplayChoices, mode.DisplayTimeoutAc);
        DisplayBattery = FindOrAdd(DisplayChoices, mode.DisplayTimeoutBattery);
        SleepAc = FindOrAdd(SleepChoices, mode.SleepTimeoutAc);
        SleepBattery = FindOrAdd(SleepChoices, mode.SleepTimeoutBattery);
        SelectedPowerPlan = PowerPlans.FirstOrDefault(plan => plan.Id == mode.PowerPlanId);
        SelectedMicrophoneMute = MicrophoneChoices.FirstOrDefault(choice =>
            choice.Setting == mode.MicrophoneMute);
        DataContext = this;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DisplayAc is null || DisplayBattery is null || SleepAc is null ||
            SleepBattery is null || SelectedPowerPlan is null || SelectedMicrophoneMute is null)
        {
            MessageBox.Show(
                LocalizationService.Translate("すべての設定を選択してください。"),
                "PC Mode Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        EditedMode = _source.Copy();
        EditedMode.DisplayTimeoutAc = DisplayAc.Seconds;
        EditedMode.DisplayTimeoutBattery = DisplayBattery.Seconds;
        EditedMode.SleepTimeoutAc = SleepAc.Seconds;
        EditedMode.SleepTimeoutBattery = SleepBattery.Seconds;
        EditedMode.PowerPlanId = SelectedPowerPlan.Id;
        EditedMode.MicrophoneMute = SelectedMicrophoneMute.Setting;
        DialogResult = true;
    }

    private static TimeoutChoice FindOrAdd(ObservableCollection<TimeoutChoice> choices, uint seconds)
    {
        var existing = choices.FirstOrDefault(choice => choice.Seconds == seconds);
        if (existing is not null)
            return existing;

        var custom = new TimeoutChoice(seconds, PCModeSwitcher.ViewModels.ModeCardViewModel.FormatTimeout(seconds));
        choices.Insert(choices.Count - 1, custom);
        return custom;
    }

    private static ObservableCollection<TimeoutChoice> CreateChoices(IEnumerable<int> minutes) =>
        new(minutes.Select(value => new TimeoutChoice(
            (uint)value * 60u,
            value switch
            {
                0 => LocalizationService.Get("Common.None"),
                60 => LocalizationService.Get("Choice.OneHour"),
                120 => LocalizationService.Get("Choice.TwoHours"),
                _ => LocalizationService.Format("Card.Minutes", value)
            })));
}
