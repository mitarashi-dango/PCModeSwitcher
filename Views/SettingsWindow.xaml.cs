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
    private readonly HashSet<string> _deletedModeIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRevertingVisibleSelection;

    public CloseButtonBehavior SelectedBehavior { get; private set; }
    public bool ShowTrayNotification { get; private set; }
    public bool StartWithWindows { get; private set; }
    public bool ShowMicrophoneControls { get; private set; }
    public string SelectedLanguage { get; private set; } = AppLanguages.System;
    public ObservableCollection<ModeSettingsItem> ModeItems { get; } = [];
    public IReadOnlyList<ModeHotkey> Hotkeys =>
        _hotkeys.Values.Where(value => !string.Equals(value.ModeId, "restore", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Copy()).ToList();
    public ModeHotkey RestoreHotkey => _hotkeys["restore"].Copy();
    public IReadOnlyList<string> SelectedVisibleModeIds =>
        ModeItems.Where(item => item.IsModeEnabled && item.IsVisible).Select(item => item.Id).ToList();
    public IReadOnlyList<string> SelectedEnabledModeIds =>
        ModeItems.Where(item => item.IsModeEnabled).Select(item => item.Id).ToList();
    public IReadOnlyList<string> DeletedModeIds => _deletedModeIds.ToList();

    public SettingsWindow(
        CloseButtonBehavior currentBehavior,
        bool showTrayNotification,
        bool startWithWindows,
        bool showMicrophoneControls,
        IReadOnlyCollection<ModeHotkey> hotkeys,
        IReadOnlyCollection<PcMode> modes,
        IReadOnlyCollection<string> visibleModeIds,
        ModeHotkey restoreHotkey,
        string language,
        Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SelectedBehavior = currentBehavior;
        ShowTrayNotification = showTrayNotification;
        StartWithWindows = startWithWindows;
        ShowMicrophoneControls = showMicrophoneControls;
        SelectedLanguage = LocalizationService.Normalize(language);
        _hotkeys = modes.ToDictionary(
            mode => mode.Id,
            mode => new ModeHotkey { ModeId = mode.Id },
            StringComparer.OrdinalIgnoreCase);
        foreach (var hotkey in hotkeys)
        {
            if (_hotkeys.ContainsKey(hotkey.ModeId))
            {
                _hotkeys[hotkey.ModeId] = hotkey.Copy();
            }
        }
        _hotkeys["restore"] = restoreHotkey.Copy();
        _hotkeys["restore"].ModeId = "restore";

        var modesById = modes.ToDictionary(mode => mode.Id, StringComparer.OrdinalIgnoreCase);
        var visibleModeIdSet = visibleModeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderedModes = visibleModeIds
            .Where(modesById.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(modeId => modesById[modeId])
            .Concat(modes.Where(mode => !visibleModeIdSet.Contains(mode.Id)));
        foreach (var mode in orderedModes)
        {
            ModeItems.Add(new ModeSettingsItem(
                mode.Id,
                mode.Name,
                mode.Icon,
                mode.IsEnabled,
                visibleModeIdSet.Contains(mode.Id),
                HotkeyValidator.Format(_hotkeys[mode.Id]),
                !SettingsService.IsBuiltInModeId(mode.Id)));
        }

        MinimizeToTrayOption.IsChecked = currentBehavior == CloseButtonBehavior.MinimizeToTray;
        ExitApplicationOption.IsChecked = currentBehavior == CloseButtonBehavior.ExitApplication;
        ShowTrayNotificationOption.IsChecked = showTrayNotification;
        StartWithWindowsOption.IsChecked = startWithWindows;
        ShowMicrophoneControlsOption.IsChecked = showMicrophoneControls;
        LanguageOption.SelectedValue = SelectedLanguage;
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

    private void DeleteMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string modeId } ||
            ModeItems.FirstOrDefault(item => string.Equals(
                item.Id,
                modeId,
                StringComparison.OrdinalIgnoreCase)) is not { CanDelete: true } item)
        {
            return;
        }

        if (MessageBox.Show(
                LocalizationService.Format("Dialog.DeleteMode", item.Name),
                LocalizationService.Get("Dialog.DeleteModeTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        ModeItems.Remove(item);
        _hotkeys.Remove(item.Id);
        _deletedModeIds.Add(item.Id);
        if (ModeItems.All(mode => !mode.IsVisible))
        {
            var replacement = ModeItems.FirstOrDefault(mode => mode.IsModeEnabled);
            if (replacement is not null)
                replacement.IsVisible = true;
        }
        HideModeSelectionValidation();
        HideShortcutValidation();
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
        if (SelectedEnabledModeIds.Count == 0)
        {
            ShowModeSelectionValidation("有効なモードを1個以上選んでください。");
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
        SelectedLanguage = LanguageOption.SelectedValue as string ?? AppLanguages.System;
        DialogResult = true;
    }

    private void UpdateHotkeyTextBoxes()
    {
        foreach (var item in ModeItems)
            item.HotkeyText = HotkeyValidator.Format(_hotkeys[item.Id]);
        RestoreHotkeyText.Text = HotkeyValidator.Format(_hotkeys["restore"]);
    }

    private void ShowModeSelectionValidation(string message)
    {
        ModeSelectionValidationMessage.Text = LocalizationService.Translate(message);
        ModeSelectionValidationMessage.Visibility = Visibility.Visible;
    }

    private void HideModeSelectionValidation()
    {
        ModeSelectionValidationMessage.Text = "";
        ModeSelectionValidationMessage.Visibility = Visibility.Collapsed;
    }

    private void ShowShortcutValidation(string message)
    {
        ShortcutValidationMessage.Text = LocalizationService.Translate(message);
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
    private bool _isModeEnabled;
    private string _hotkeyText;

    public ModeSettingsItem(
        string id,
        string name,
        string icon,
        bool isModeEnabled,
        bool isVisible,
        string hotkeyText,
        bool canDelete)
    {
        Id = id;
        Name = name;
        Icon = icon;
        _isModeEnabled = isModeEnabled;
        _isVisible = isVisible;
        _hotkeyText = hotkeyText;
        CanDelete = canDelete;
    }

    public string Id { get; }
    public string Name { get; }
    public string Icon { get; }
    public bool CanDelete { get; }
    public bool HasCustomIcon => ModeIconAssets.HasCustomIcon(Id, Icon);
    public string? CustomIconSource => ModeIconAssets.GetCustomIconSource(Id, Icon);
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
    public bool IsModeEnabled
    {
        get => _isModeEnabled;
        set
        {
            if (!SetProperty(ref _isModeEnabled, value)) return;
            if (!value) IsVisible = false;
        }
    }
    public string HotkeyText
    {
        get => _hotkeyText;
        set => SetProperty(ref _hotkeyText, value);
    }
}
