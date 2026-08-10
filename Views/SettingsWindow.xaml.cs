using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCModeSwitcher.Models;
using PCModeSwitcher.Services;

namespace PCModeSwitcher.Views;

public partial class SettingsWindow : Window
{
    private readonly Dictionary<string, ModeHotkey> _hotkeys;

    public CloseButtonBehavior SelectedBehavior { get; private set; }
    public bool ShowTrayNotification { get; private set; }
    public bool StartWithWindows { get; private set; }
    public IReadOnlyList<ModeHotkey> Hotkeys => _hotkeys.Values.Select(hotkey => hotkey.Copy()).ToList();

    public SettingsWindow(
        CloseButtonBehavior currentBehavior,
        bool showTrayNotification,
        bool startWithWindows,
        IReadOnlyCollection<ModeHotkey> hotkeys,
        Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SelectedBehavior = currentBehavior;
        ShowTrayNotification = showTrayNotification;
        StartWithWindows = startWithWindows;
        _hotkeys = SettingsService.CreateDefaultHotkeys()
            .ToDictionary(hotkey => hotkey.ModeId, StringComparer.OrdinalIgnoreCase);
        foreach (var hotkey in hotkeys)
        {
            if (_hotkeys.ContainsKey(hotkey.ModeId))
            {
                _hotkeys[hotkey.ModeId] = hotkey.Copy();
            }
        }

        MinimizeToTrayOption.IsChecked = currentBehavior == CloseButtonBehavior.MinimizeToTray;
        ExitApplicationOption.IsChecked = currentBehavior == CloseButtonBehavior.ExitApplication;
        ShowTrayNotificationOption.IsChecked = showTrayNotification;
        StartWithWindowsOption.IsChecked = startWithWindows;
        UpdateHotkeyTextBoxes();
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
        DialogResult = true;
    }

    private void UpdateHotkeyTextBoxes()
    {
        GameHotkeyTextBox.Text = HotkeyValidator.Format(_hotkeys["game"]);
        WorkHotkeyTextBox.Text = HotkeyValidator.Format(_hotkeys["work"]);
        NormalHotkeyTextBox.Text = HotkeyValidator.Format(_hotkeys["normal"]);
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
