using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCModeSwitcher.Models;
using PCModeSwitcher.Services;
using PCModeSwitcher.ViewModels;

namespace PCModeSwitcher.Views;

public partial class SettingsWindow : Window
{
    private readonly Dictionary<string, ModeHotkey> _hotkeys;
    private bool _isRevertingVisibleSelection;

    public CloseButtonBehavior SelectedBehavior { get; private set; }
    public bool ShowTrayNotification { get; private set; }
    public bool StartWithWindows { get; private set; }
    public bool ShowMicrophoneControls { get; private set; }
    public ObservableCollection<ModeSettingsItem> ModeItems { get; } = [];
    public IReadOnlyList<ModeHotkey> Hotkeys =>
        ModeItems.Select(item => _hotkeys[item.Id].Copy()).ToList();
    public IReadOnlyList<string> SelectedVisibleModeIds =>
        ModeItems.Where(item => item.IsVisible).Select(item => item.Id).ToList();

    public SettingsWindow(
        CloseButtonBehavior currentBehavior,
        bool showTrayNotification,
        bool startWithWindows,
        bool showMicrophoneControls,
        IReadOnlyCollection<ModeHotkey> hotkeys,
        IReadOnlyCollection<PcMode> modes,
        IReadOnlyCollection<string> visibleModeIds,
        Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SelectedBehavior = currentBehavior;
        ShowTrayNotification = showTrayNotification;
        StartWithWindows = startWithWindows;
        ShowMicrophoneControls = showMicrophoneControls;
        _hotkeys = SettingsService.CreateDefaultHotkeys()
            .ToDictionary(hotkey => hotkey.ModeId, StringComparer.OrdinalIgnoreCase);
        foreach (var hotkey in hotkeys)
        {
            if (_hotkeys.ContainsKey(hotkey.ModeId))
            {
                _hotkeys[hotkey.ModeId] = hotkey.Copy();
            }
        }

        foreach (var modeId in SettingsService.SupportedModeIds)
        {
            var mode = modes.First(value =>
                string.Equals(value.Id, modeId, StringComparison.OrdinalIgnoreCase));
            ModeItems.Add(new ModeSettingsItem(
                mode.Id,
                mode.Name,
                mode.Icon,
                visibleModeIds.Contains(mode.Id, StringComparer.OrdinalIgnoreCase),
                HotkeyValidator.Format(_hotkeys[mode.Id])));
        }

        MinimizeToTrayOption.IsChecked = currentBehavior == CloseButtonBehavior.MinimizeToTray;
        ExitApplicationOption.IsChecked = currentBehavior == CloseButtonBehavior.ExitApplication;
        ShowTrayNotificationOption.IsChecked = showTrayNotification;
        StartWithWindowsOption.IsChecked = startWithWindows;
        ShowMicrophoneControlsOption.IsChecked = showMicrophoneControls;
        UpdateHotkeyTextBoxes();
    }

    private void VisibleModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isRevertingVisibleSelection ||
            sender is not CheckBox { DataContext: ModeSettingsItem item } checkBox)
        {
            return;
        }

        item.IsVisible = checkBox.IsChecked == true;
        var selectedCount = ModeItems.Count(mode => mode.IsVisible);
        if (selectedCount > SettingsService.MaximumVisibleModeCount)
        {
            _isRevertingVisibleSelection = true;
            item.IsVisible = false;
            checkBox.IsChecked = false;
            _isRevertingVisibleSelection = false;
            ShowModeSelectionValidation("アプリ画面に表示できるモードは最大5個です。");
            return;
        }

        if (selectedCount == 0)
        {
            ShowModeSelectionValidation("表示するモードを1個以上選んでください。");
            return;
        }

        HideModeSelectionValidation();
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBox { Tag: string modeId })
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
        {
            return;
        }

        if ((key is Key.Delete or Key.Back) && Keyboard.Modifiers == ModifierKeys.None)
        {
            ClearHotkey(modeId);
            return;
        }

        var modifiers = ToHotkeyModifiers(Keyboard.Modifiers);
        if (modifiers == HotkeyModifiers.None)
        {
            ShowShortcutValidation("Ctrl、Alt、Shift、Winのいずれかを同時に押してください。");
            return;
        }

        var candidate = new ModeHotkey
        {
            ModeId = modeId,
            Modifiers = modifiers,
            VirtualKey = KeyInterop.VirtualKeyFromKey(key)
        };
        var previous = _hotkeys[modeId];
        _hotkeys[modeId] = candidate;
        var validation = HotkeyValidator.Validate(_hotkeys.Values.ToList());
        if (!validation.IsSuccess)
        {
            _hotkeys[modeId] = previous;
            ShowShortcutValidation(validation.UserMessage);
            return;
        }

        HideShortcutValidation();
        UpdateHotkeyTextBoxes();
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string modeId })
        {
            ClearHotkey(modeId);
        }
    }

    private void ClearHotkey(string modeId)
    {
        _hotkeys[modeId] = new ModeHotkey { ModeId = modeId };
        HideShortcutValidation();
        UpdateHotkeyTextBoxes();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedVisibleModeIds.Count is < 1 or > SettingsService.MaximumVisibleModeCount)
        {
            ShowModeSelectionValidation("アプリ画面に表示するモードは1〜5個で選んでください。");
            return;
        }

        var validation = HotkeyValidator.Validate(_hotkeys.Values.ToList());
        if (!validation.IsSuccess)
        {
            ShowShortcutValidation(validation.UserMessage);
            return;
        }

        SelectedBehavior = MinimizeToTrayOption.IsChecked == true
            ? CloseButtonBehavior.MinimizeToTray
            : CloseButtonBehavior.ExitApplication;
        ShowTrayNotification = ShowTrayNotificationOption.IsChecked == true;
        StartWithWindows = StartWithWindowsOption.IsChecked == true;
        ShowMicrophoneControls = ShowMicrophoneControlsOption.IsChecked == true;
        DialogResult = true;
    }

    private void UpdateHotkeyTextBoxes()
    {
        foreach (var item in ModeItems)
            item.HotkeyText = HotkeyValidator.Format(_hotkeys[item.Id]);
    }

    private void ShowModeSelectionValidation(string message)
    {
        ModeSelectionValidationMessage.Text = message;
        ModeSelectionValidationMessage.Visibility = Visibility.Visible;
    }

    private void HideModeSelectionValidation()
    {
        ModeSelectionValidationMessage.Text = "";
        ModeSelectionValidationMessage.Visibility = Visibility.Collapsed;
    }

    private void ShowShortcutValidation(string message)
    {
        ShortcutValidationMessage.Text = message;
        ShortcutValidationMessage.Visibility = Visibility.Visible;
    }

    private void HideShortcutValidation()
    {
        ShortcutValidationMessage.Text = "";
        ShortcutValidationMessage.Visibility = Visibility.Collapsed;
    }

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
            result |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows))
            result |= HotkeyModifiers.Windows;
        return result;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;
}

public sealed class ModeSettingsItem : ObservableObject
{
    private bool _isVisible;
    private string _hotkeyText;

    public ModeSettingsItem(
        string id,
        string name,
        string icon,
        bool isVisible,
        string hotkeyText)
    {
        Id = id;
        Name = name;
        Icon = icon;
        _isVisible = isVisible;
        _hotkeyText = hotkeyText;
    }

    public string Id { get; }
    public string Name { get; }
    public string Icon { get; }
    public bool HasCustomIcon => ModeIconAssets.HasCustomIcon(Id);
    public string? CustomIconSource => ModeIconAssets.GetCustomIconSource(Id);
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
    public string HotkeyText
    {
        get => _hotkeyText;
        set => SetProperty(ref _hotkeyText, value);
    }
}
