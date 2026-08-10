using PCModeSwitcher.Services;
using PCModeSwitcher.Models;
using PCModeSwitcher.ViewModels;
using PCModeSwitcher.Views;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;

namespace PCModeSwitcher;

public partial class App : Wpf.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _applicationIcon;
    private MainWindow? _mainWindow;
    private bool _trayHintShown;

    protected override async void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = Wpf.ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += (_, args) =>
        {
            Wpf.MessageBox.Show(
                "予期しない問題が発生しました。操作を中止しました。",
                "PC Mode Switcher",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Error);
            args.Handled = true;
        };

        var settingsService = new SettingsService();
        var powerService = new PowerSettingsService();
        var viewModel = new MainViewModel(settingsService, powerService);
        var window = new MainWindow { DataContext = viewModel };
        _mainWindow = window;
        MainWindow = window;
        CreateTrayIcon();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CloseButtonBehavior))
            {
                UpdateTrayIconVisibility(viewModel.CloseButtonBehavior);
            }
        };
        window.HiddenToTray += OnWindowHiddenToTray;
        SessionEnding += (_, _) => window.AllowClose();
        window.Show();
        await viewModel.InitializeAsync();
        UpdateTrayIconVisibility(viewModel.CloseButtonBehavior);
    }

    protected override void OnExit(Wpf.ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _applicationIcon?.Dispose();
        _applicationIcon = null;

        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("表示");
        showItem.Click += (_, _) => Dispatcher.Invoke(RestoreMainWindow);

        var exitItem = new Forms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitApplication);

        menu.Items.Add(showItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _applicationIcon = Environment.ProcessPath is { } executablePath
            ? System.Drawing.Icon.ExtractAssociatedIcon(executablePath)
            : null;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
            Text = "PC Mode Switcher",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(RestoreMainWindow);
            }
        };
    }

    private void RestoreMainWindow()
    {
        _mainWindow?.RestoreFromTray();
    }

    private void UpdateTrayIconVisibility(CloseButtonBehavior behavior)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = behavior == CloseButtonBehavior.MinimizeToTray;
        }
    }

    private void ExitApplication()
    {
        _mainWindow?.AllowClose();
        Shutdown();
    }

    private void OnWindowHiddenToTray(object? sender, EventArgs e)
    {
        var showNotification = _mainWindow?.DataContext is MainViewModel viewModel &&
            viewModel.ShowTrayNotification;
        if (!showNotification || _trayHintShown || _trayIcon is null || !_trayIcon.Visible)
        {
            return;
        }

        _trayHintShown = true;
        _trayIcon.ShowBalloonTip(
            3000,
            "PC Mode Switcher",
            "通知領域に格納しました。ダブルクリックで表示、右クリックの［終了］で終了できます。",
            Forms.ToolTipIcon.Info);
    }
}
