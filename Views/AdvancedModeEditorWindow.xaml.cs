using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using PCModeSwitcher.Models;
using PCModeSwitcher.Services;
using PCModeSwitcher.ViewModels;

namespace PCModeSwitcher.Views;

public partial class AdvancedModeEditorWindow : Window, INotifyPropertyChanged
{
    private const uint NoChange = uint.MaxValue;
    private readonly AdvancedModeEditSession _editSession;
    private readonly DisplayModeService _displayService = new();
    private readonly WindowPlacementService _windowService = new();
    private DisplayModeInfo? _selectedDisplay;
    private uint? _selectedRefreshRate;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string ModeName { get; set; }
    public string ModeIcon { get; set; }
    public bool IsModeEnabled { get; set; }
    public bool HasBattery { get; }
    public ObservableCollection<TimeoutChoice> TimeoutChoices { get; } = CreateTimeouts();
    public TimeoutChoice? DisplayAc { get; set; }
    public TimeoutChoice? DisplayDc { get; set; }
    public TimeoutChoice? SleepAc { get; set; }
    public TimeoutChoice? SleepDc { get; set; }
    public ObservableCollection<PowerPlan> PowerPlans { get; }
    public bool ChangePowerPlan { get; set; }
    public PowerPlan? SelectedPowerPlan { get; set; }
    public ObservableCollection<EnumChoice<WindowsPowerMode>> PowerModes { get; } =
    [
        new(WindowsPowerMode.NoChange, LocalizationService.Get("Common.NoChange")), new(WindowsPowerMode.BestEfficiency, LocalizationService.Get("Choice.BestEfficiency")),
        new(WindowsPowerMode.Balanced, LocalizationService.Get("Choice.Balanced")), new(WindowsPowerMode.BestPerformance, LocalizationService.Get("Choice.BestPerformance"))
    ];
    public EnumChoice<WindowsPowerMode>? AcPowerMode { get; set; }
    public EnumChoice<WindowsPowerMode>? DcPowerMode { get; set; }
    public ObservableCollection<EnumChoice<SleepPreventionMode>> SleepPreventions { get; } =
    [new(SleepPreventionMode.None, LocalizationService.Get("Choice.Disabled")), new(SleepPreventionMode.System, LocalizationService.Get("Choice.PreventSleep")), new(SleepPreventionMode.SystemAndDisplay, LocalizationService.Get("Choice.PreventSleepDisplay"))];
    public EnumChoice<SleepPreventionMode>? SleepPrevention { get; set; }

    public ObservableCollection<DisplayModeInfo> Displays { get; } = [];
    public DisplayModeInfo? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            _selectedDisplay = value;
            RefreshRates.Clear();
            if (value is not null) foreach (var rate in value.SupportedRefreshRates) RefreshRates.Add(rate);
            SelectedRefreshRate = value is not null && _editSession.Draft.Display.RefreshRate is { } requested && RefreshRates.Contains(requested)
                ? requested : value?.CurrentRefreshRate;
            Changed(); Changed(nameof(DisplayTrustText));
        }
    }
    public ObservableCollection<uint> RefreshRates { get; } = [];
    public uint? SelectedRefreshRate
    {
        get => _selectedRefreshRate;
        set
        {
            if (_selectedRefreshRate == value) return;
            _selectedRefreshRate = value;
            Changed(); Changed(nameof(DisplayTrustText));
        }
    }
    public string DisplayTrustText => IsSelectedDisplayTrusted
        ? LocalizationService.Get("Advanced.Verified")
        : LocalizationService.Get("Advanced.Unverified");

    public ObservableCollection<EnumChoice<AudioMuteSetting>> MuteChoices { get; } =
    [new(AudioMuteSetting.NoChange, LocalizationService.Get("Common.NoChange")), new(AudioMuteSetting.Mute, LocalizationService.Get("Choice.Mute")), new(AudioMuteSetting.Unmute, LocalizationService.Get("Choice.Unmute"))];
    public string OutputVolume { get; set; }
    public string MicrophoneVolume { get; set; }
    public EnumChoice<AudioMuteSetting>? OutputMute { get; set; }
    public EnumChoice<AudioMuteSetting>? MicrophoneMute { get; set; }

    public ObservableCollection<LaunchEditorRow> LaunchItems { get; } = [];
    public ObservableCollection<CloseEditorRow> CloseRules { get; } = [];
    public ObservableCollection<MonitorEditorRow> MonitorRules { get; } = [];
    public LaunchEditorRow? SelectedLaunchItem { get; set; }
    public CloseEditorRow? SelectedCloseRule { get; set; }
    public MonitorEditorRow? SelectedMonitorRule { get; set; }
    public string WindowSummary => LocalizationService.Format("Advanced.SavedWindows", _editSession.Draft.WindowPlacements.Count);
    public PcMode? EditedMode { get; private set; }

    public AdvancedModeEditorWindow(PcMode mode, IReadOnlyList<PowerPlan> plans, bool hasBattery, Window owner)
    {
        _editSession = new(mode);
        var draft = _editSession.Draft;
        InitializeComponent(); Owner = owner;
        ModeName = draft.Name; ModeIcon = draft.Icon; IsModeEnabled = draft.IsEnabled; HasBattery = hasBattery;
        PowerPlans = new(plans); ChangePowerPlan = draft.Power.ChangePowerPlan;
        SelectedPowerPlan = PowerPlans.FirstOrDefault(value => value.Id == draft.Power.PowerPlanId || value.Id == draft.PowerPlanId);
        AcPowerMode = PowerModes.First(value => value.Value == draft.Power.AcPowerMode);
        DcPowerMode = PowerModes.First(value => value.Value == draft.Power.DcPowerMode);
        SleepPrevention = SleepPreventions.First(value => value.Value == draft.Power.SleepPrevention);
        DisplayAc = FindTimeout(draft.Power.DisplayTimeoutAcSeconds); DisplayDc = FindTimeout(draft.Power.DisplayTimeoutDcSeconds);
        SleepAc = FindTimeout(draft.Power.SleepTimeoutAcSeconds); SleepDc = FindTimeout(draft.Power.SleepTimeoutDcSeconds);
        OutputVolume = draft.Audio.Output.VolumePercent?.ToString() ?? "";
        MicrophoneVolume = draft.Audio.Microphone.VolumePercent?.ToString() ?? "";
        OutputMute = MuteChoices.First(value => value.Value == draft.Audio.Output.Mute);
        MicrophoneMute = MuteChoices.First(value => value.Value == draft.Audio.Microphone.Mute);
        foreach (var item in draft.LaunchItems) LaunchItems.Add(LaunchEditorRow.From(item));
        foreach (var rule in draft.CloseProcessRules) CloseRules.Add(CloseEditorRow.From(rule));
        foreach (var rule in draft.MonitorRules) MonitorRules.Add(new() { ExecutablePathOrName = rule.ExecutablePathOrName });
        var displayResult = _displayService.GetDisplays();
        if (displayResult.Value is not null) foreach (var display in displayResult.Value) Displays.Add(display);
        SelectedDisplay = Displays.FirstOrDefault(value => string.Equals(value.DeviceName, draft.Display.DeviceName, StringComparison.OrdinalIgnoreCase));
        DataContext = this;
    }

    private void TestDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDisplay is null || SelectedRefreshRate is null) { Warn("モニターとリフレッシュレートを選択してください。"); return; }
        var original = _displayService.GetCurrentMode(SelectedDisplay.DeviceName);
        if (!original.IsSuccess || original.Value is null) { Warn(original.UserMessage); return; }
        var test = _displayService.ApplyRefreshRate(SelectedDisplay.DeviceName, SelectedRefreshRate.Value, true);
        if (!test.IsSuccess) { Warn(test.UserMessage); return; }
        var apply = _displayService.ApplyRefreshRate(SelectedDisplay.DeviceName, SelectedRefreshRate.Value);
        if (!apply.IsSuccess) { Warn(apply.UserMessage); return; }
        if (new DisplayConfirmationWindow(SelectedRefreshRate.Value) { Owner = this }.ShowDialog() != true)
        { _displayService.Restore(original.Value); return; }
        _editSession.ConfirmDisplay(SelectedDisplay.DeviceName, SelectedRefreshRate.Value, SelectedDisplay.HardwareSignature);
        Changed(nameof(DisplayTrustText));
    }

    private void CaptureWindows_Click(object sender, RoutedEventArgs e)
    {
        var result = _windowService.CaptureVisibleWindows();
        if (!result.IsSuccess || result.Value is null) { Warn(result.UserMessage); return; }
        _editSession.ReplaceWindowPlacements(result.Value); Changed(nameof(WindowSummary));
    }
    private void AddLaunch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = LocalizationService.Get("File.ExecutableAllFilter") };
        if (dialog.ShowDialog(this) == true) LaunchItems.Add(new() { Target = dialog.FileName, WorkingDirectory = System.IO.Path.GetDirectoryName(dialog.FileName) ?? "" });
    }
    private void AddUri_Click(object sender, RoutedEventArgs e) => LaunchItems.Add(new() { Target = "steam://" });
    private void RemoveLaunch_Click(object sender, RoutedEventArgs e) { if (SelectedLaunchItem is not null) LaunchItems.Remove(SelectedLaunchItem); }
    private void AddClose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = LocalizationService.Get("File.ExecutableFilter") };
        if (dialog.ShowDialog(this) == true) CloseRules.Add(new() { ExecutablePath = dialog.FileName });
    }
    private void RemoveClose_Click(object sender, RoutedEventArgs e) { if (SelectedCloseRule is not null) CloseRules.Remove(SelectedCloseRule); }
    private void AddMonitor_Click(object sender, RoutedEventArgs e) => MonitorRules.Add(new());
    private void RemoveMonitor_Click(object sender, RoutedEventArgs e) { if (SelectedMonitorRule is not null) MonitorRules.Remove(SelectedMonitorRule); }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ModeName)) { Warn("モード名を入力してください。"); return; }
        if (ChangePowerPlan && SelectedPowerPlan is null) { Warn("電源プランを選択してください。"); return; }
        if (!ParseVolume(OutputVolume, out var outputVolume) || !ParseVolume(MicrophoneVolume, out var microphoneVolume))
        { Warn("音量は空欄（変更しない）または0～100で入力してください。"); return; }
        if (CloseRules.Any(value => value.AllowForceKill) && MessageBox.Show(
            LocalizationService.Get("Dialog.ForceKill"), LocalizationService.Get("Dialog.ForceKillTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var value = _editSession.Draft.Copy(); value.Name = ModeName.Trim(); value.Icon = string.IsNullOrWhiteSpace(ModeIcon) ? "●" : ModeIcon.Trim(); value.IsEnabled = IsModeEnabled;
        value.Power.ChangePowerPlan = ChangePowerPlan; if (SelectedPowerPlan is not null) value.Power.PowerPlanId = SelectedPowerPlan.Id;
        value.Power.AcPowerMode = AcPowerMode?.Value ?? WindowsPowerMode.NoChange; value.Power.DcPowerMode = DcPowerMode?.Value ?? WindowsPowerMode.NoChange;
        value.Power.SleepPrevention = SleepPrevention?.Value ?? SleepPreventionMode.None;
        value.Power.DisplayTimeoutAcSeconds = NullableTimeout(DisplayAc); value.Power.DisplayTimeoutDcSeconds = NullableTimeout(DisplayDc);
        value.Power.SleepTimeoutAcSeconds = NullableTimeout(SleepAc); value.Power.SleepTimeoutDcSeconds = NullableTimeout(SleepDc);
        value.PowerPlanId = value.Power.PowerPlanId;
        if (value.Power.DisplayTimeoutAcSeconds is { } displayAc) value.DisplayTimeoutAc = displayAc;
        if (value.Power.DisplayTimeoutDcSeconds is { } displayDc) value.DisplayTimeoutBattery = displayDc;
        if (value.Power.SleepTimeoutAcSeconds is { } sleepAc) value.SleepTimeoutAc = sleepAc;
        if (value.Power.SleepTimeoutDcSeconds is { } sleepDc) value.SleepTimeoutBattery = sleepDc;
        var displayIsTrusted = IsSelectedDisplayTrusted;
        value.Display.DeviceName = SelectedDisplay?.DeviceName; value.Display.RefreshRate = SelectedDisplay is null ? null : SelectedRefreshRate;
        value.Display.IsTrusted = displayIsTrusted;
        value.Display.HardwareSignature = displayIsTrusted ? SelectedDisplay!.HardwareSignature : null;
        value.Audio.Output.VolumePercent = outputVolume; value.Audio.Output.Mute = OutputMute?.Value ?? AudioMuteSetting.NoChange;
        value.Audio.Microphone.VolumePercent = microphoneVolume; value.Audio.Microphone.Mute = MicrophoneMute?.Value ?? AudioMuteSetting.NoChange;
        value.MicrophoneMute = value.Audio.Microphone.Mute switch { AudioMuteSetting.Mute => MicrophoneMuteSetting.Mute, AudioMuteSetting.Unmute => MicrophoneMuteSetting.Unmute, _ => MicrophoneMuteSetting.NoChange };
        value.LaunchItems = LaunchItems.Select(row => row.ToModel()).ToList(); value.CloseProcessRules = CloseRules.Select(row => row.ToModel()).ToList();
        value.MonitorRules = MonitorRules.Where(row => !string.IsNullOrWhiteSpace(row.ExecutablePathOrName)).Select(row => new ProcessMonitorRule { ExecutablePathOrName = row.ExecutablePathOrName.Trim() }).ToList();
        EditedMode = value; DialogResult = true;
    }

    private bool IsSelectedDisplayTrusted => _editSession.IsDisplayTrusted(
        SelectedDisplay?.DeviceName, SelectedRefreshRate, SelectedDisplay?.HardwareSignature);

    private TimeoutChoice FindTimeout(uint? seconds)
    {
        var raw = seconds ?? NoChange; var found = TimeoutChoices.FirstOrDefault(value => value.Seconds == raw);
        if (found is not null) return found; found = new(raw, ViewModels.ModeCardViewModel.FormatTimeout(raw)); TimeoutChoices.Insert(TimeoutChoices.Count - 1, found); return found;
    }
    private static uint? NullableTimeout(TimeoutChoice? value) => value?.Seconds == NoChange ? null : value?.Seconds;
    private static bool ParseVolume(string text, out int? value) { value = null; if (string.IsNullOrWhiteSpace(text)) return true; if (!int.TryParse(text, out var parsed) || parsed is < 0 or > 100) return false; value = parsed; return true; }
    private static ObservableCollection<TimeoutChoice> CreateTimeouts() => new(new[] { NoChange, 60u, 120u, 180u, 300u, 600u, 900u, 1200u, 1800u, 2700u, 3600u, 7200u, 10800u, 0u }.Select(value => new TimeoutChoice(value, value switch { NoChange => LocalizationService.Get("Common.NoChange"), 0 => LocalizationService.Get("Common.None"), 3600 => LocalizationService.Get("Choice.OneHour"), 7200 => LocalizationService.Get("Choice.TwoHours"), 10800 => LocalizationService.Get("Choice.ThreeHours"), _ => LocalizationService.Format("Card.Minutes", value / 60) })));
    private void Warn(string message) => MessageBox.Show(LocalizationService.Translate(message), "PC Mode Switcher", MessageBoxButton.OK, MessageBoxImage.Warning);
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record EnumChoice<T>(T Value, string Label) { public override string ToString() => Label; }
public sealed class LaunchEditorRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Target { get; set; } = ""; public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = ""; public int DelayMilliseconds { get; set; } public int Order { get; set; }
    public bool AllowAdditionalInstance { get; set; } public bool CloseOnRestore { get; set; }
    public static LaunchEditorRow From(LaunchItem value) => new() { Id=value.Id,Target=value.Target,Arguments=string.Join(" | ",value.Arguments),WorkingDirectory=value.WorkingDirectory??"",DelayMilliseconds=value.DelayMilliseconds,Order=value.Order,AllowAdditionalInstance=value.AllowAdditionalInstance,CloseOnRestore=value.CloseOnRestore };
    public LaunchItem ToModel() => new() { Id=Id,Target=Target.Trim(),Arguments=Split(Arguments),WorkingDirectory=string.IsNullOrWhiteSpace(WorkingDirectory)?null:WorkingDirectory.Trim(),DelayMilliseconds=Math.Max(0,DelayMilliseconds),Order=Order,AllowAdditionalInstance=AllowAdditionalInstance,CloseOnRestore=CloseOnRestore };
    internal static List<string> Split(string value) => value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
public sealed class CloseEditorRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string ExecutablePath { get; set; } = ""; public string RestartArguments { get; set; } = "";
    public string RestartWorkingDirectory { get; set; } = ""; public int GracePeriodSeconds { get; set; } = 5; public bool AllowForceKill { get; set; } public bool RestartOnRestore { get; set; }
    public static CloseEditorRow From(CloseProcessRule value) => new() { Id=value.Id,ExecutablePath=value.ExecutablePath,RestartArguments=string.Join(" | ",value.RestartArguments),RestartWorkingDirectory=value.RestartWorkingDirectory??"",GracePeriodSeconds=value.GracePeriodSeconds,AllowForceKill=value.AllowForceKill,RestartOnRestore=value.RestartOnRestore };
    public CloseProcessRule ToModel() => new() { Id=Id,ExecutablePath=ExecutablePath.Trim(),RestartArguments=LaunchEditorRow.Split(RestartArguments),RestartWorkingDirectory=string.IsNullOrWhiteSpace(RestartWorkingDirectory)?null:RestartWorkingDirectory.Trim(),GracePeriodSeconds=Math.Clamp(GracePeriodSeconds,0,300),AllowForceKill=AllowForceKill,RestartOnRestore=RestartOnRestore };
}
public sealed class MonitorEditorRow { public string ExecutablePathOrName { get; set; } = ""; }
