using System.ComponentModel;
using System.Windows;
using PCModeSwitcher.Models;
using PCModeSwitcher.ViewModels;

namespace PCModeSwitcher.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public event EventHandler? HiddenToTray;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    protected override async void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (DataContext is MainViewModel viewModel)
            await viewModel.RefreshCurrentModeAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var minimizeToTray = DataContext is not MainViewModel viewModel ||
            viewModel.CloseButtonBehavior == CloseButtonBehavior.MinimizeToTray;
        if (!_allowClose && minimizeToTray)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var settingsWindow = new SettingsWindow(
            viewModel.CloseButtonBehavior,
            viewModel.ShowTrayNotification,
            viewModel.StartWithWindows,
            viewModel.Hotkeys,
            this);
        if (settingsWindow.ShowDialog() != true)
        {
            return;
        }

        var result = await viewModel.SetAppPreferencesAsync(
            settingsWindow.SelectedBehavior,
            settingsWindow.ShowTrayNotification,
            settingsWindow.StartWithWindows,
            settingsWindow.Hotkeys);
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                result.UserMessage,
                "PC Mode Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
