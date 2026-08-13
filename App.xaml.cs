using PCModeSwitcher.Services;
using PCModeSwitcher.Models;
using PCModeSwitcher.ViewModels;
using PCModeSwitcher.Views;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfInterop = System.Windows.Interop;

namespace PCModeSwitcher;

public partial class App : Wpf.Application
{
    private const string SingleInstanceId = "PCModeSwitcher.8F75438A-DB7F-48CD-A753-AD477D251D8F";

    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _trayModeItems =
        new(StringComparer.OrdinalIgnoreCase);
    private System.Drawing.Icon? _applicationIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private GlobalHotkeyService? _globalHotkeyService;
    private bool _trayHintShown;
    private bool _isWindowHiddenToTray;

    protected override async void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);
        _isWindowHiddenToTray = IsStartupLaunch(e.Args);
        _singleInstanceCoordinator = new SingleInstanceCoordinator(SingleInstanceId);
        _singleInstanceCoordinator.ActivationRequested += OnActivationRequested;
        if (!_singleInstanceCoordinator.TryAcquire())
        {
            Shutdown();
            return;
        }

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
        var microphoneMuteService = new MicrophoneMuteService();
        var startupService = new StartupService();
        _globalHotkeyService = new GlobalHotkeyService();
        var viewModel = new MainViewModel(
            settingsService,
            powerService,
            microphoneMuteService,
            startupService,
            _globalHotkeyService);
        var window = new MainWindow { DataContext = viewModel };
        _mainWindow = window;
        _mainViewModel = viewModel;
        MainWindow = window;
        _globalHotkeyService.Attach(window);
        _globalHotkeyService.HotkeyPressed += OnHotkeyPressed;
        CreateTrayIcon();
        viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        window.HiddenToTray += OnWindowHiddenToTray;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SessionEnding += (_, _) => window.AllowClose();
        if (_isWindowHiddenToTray)
        {
            // グローバルショートカット登録にはHWNDが必要。EnsureHandleは画面を表示しない。
            new WpfInterop.WindowInteropHelper(window).EnsureHandle();
        }
        else
        {
            window.Show();
        }

        await viewModel.InitializeAsync();
        CreateTrayModeItems();
        UpdateTrayModeState();
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

        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayModeItems.Clear();

        _applicationIcon?.Dispose();
        _applicationIcon = null;

        if (_globalHotkeyService is not null)
        {
            _globalHotkeyService.HotkeyPressed -= OnHotkeyPressed;
            _globalHotkeyService.Dispose();
            _globalHotkeyService = null;
        }

        if (_mainViewModel is not null)
        {
            _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        }
        _mainViewModel = null;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_singleInstanceCoordinator is not null)
        {
            _singleInstanceCoordinator.ActivationRequested -= OnActivationRequested;
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
        }

        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new Forms.ContextMenuStrip();
        var loadingItem = new Forms.ToolStripMenuItem("モードを読み込んでいます…")
        {
            Enabled = false,
            Tag = "loading"
        };
        var showItem = new Forms.ToolStripMenuItem("表示");
        showItem.Click += (_, _) => Dispatcher.Invoke(RestoreMainWindow);

        var exitItem = new Forms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitApplication);

        _trayMenu.Items.Add(loadingItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator { Tag = "mode-separator" });
        _trayMenu.Items.Add(showItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _applicationIcon = Environment.ProcessPath is { } executablePath
            ? System.Drawing.Icon.ExtractAssociatedIcon(executablePath)
            : null;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
            Text = "PC Mode Switcher",
            ContextMenuStrip = _trayMenu,
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

    private void CreateTrayModeItems()
    {
        if (_trayMenu is null || _mainViewModel is null)
        {
            return;
        }

        var loadingItem = _trayMenu.Items
            .OfType<Forms.ToolStripItem>()
            .FirstOrDefault(item => Equals(item.Tag, "loading"));
        if (loadingItem is not null)
        {
            _trayMenu.Items.Remove(loadingItem);
            loadingItem.Dispose();
        }

        var insertIndex = 0;
        foreach (var mode in _mainViewModel.Modes)
        {
            var modeId = mode.Mode.Id;
            var modeItem = new Forms.ToolStripMenuItem($"{mode.Icon}  {mode.Name}")
            {
                CheckOnClick = false,
                Tag = modeId
            };
            modeItem.Click += (_, _) => Dispatcher.BeginInvoke(
                new Action(async () => await ApplyModeFromTrayAsync(modeId)));
            _trayMenu.Items.Insert(insertIndex++, modeItem);
            _trayModeItems[modeId] = modeItem;
        }
    }

    private void RestoreMainWindow()
    {
        _isWindowHiddenToTray = false;
        _mainWindow?.RestoreFromTray();
        if (_mainViewModel is not null)
        {
            UpdateTrayIconVisibility(_mainViewModel.CloseButtonBehavior);
        }
    }

    private void OnActivationRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RestoreMainWindow);
    }

    private void OnHotkeyPressed(object? sender, ModeHotkeyPressedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(async () => await ApplyModeFromTrayAsync(e.ModeId)));
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_mainViewModel is not null)
                await _mainViewModel.RefreshCurrentModeAsync();
        }));
    }

    private void UpdateTrayIconVisibility(CloseButtonBehavior behavior)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = _isWindowHiddenToTray ||
                behavior == CloseButtonBehavior.MinimizeToTray;
        }
    }

    private void ExitApplication()
    {
        _mainWindow?.AllowClose();
        Shutdown();
    }

    private void OnWindowHiddenToTray(object? sender, EventArgs e)
    {
        _isWindowHiddenToTray = true;
        if (_mainViewModel is not null)
        {
            UpdateTrayIconVisibility(_mainViewModel.CloseButtonBehavior);
        }

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

    private async Task ApplyModeFromTrayAsync(string modeId)
    {
        if (_mainViewModel is null)
        {
            return;
        }

        var mode = _mainViewModel.Modes.FirstOrDefault(card =>
            string.Equals(card.Mode.Id, modeId, StringComparison.OrdinalIgnoreCase));
        if (mode is null)
        {
            return;
        }

        var result = await _mainViewModel.ApplyModeByIdAsync(modeId);
        if (result is null || _trayIcon is null || !_trayIcon.Visible)
        {
            return;
        }

        var title = result.IsSuccess
            ? $"{mode.Name}モードに切り替えました"
            : result.Steps.Any(step => step.IsSuccess && !step.IsSkipped)
                ? $"{mode.Name}モードを一部適用しました"
                : $"{mode.Name}モードを適用できませんでした";
        var message = result.IsSuccess
            ? "モードに登録された設定を適用しました。"
            : string.Join("\n", result.Steps.Select(step =>
                $"{(step.IsSkipped ? "–" : step.IsSuccess ? "✓" : "×")} {step.Name}"));
        _trayIcon.ShowBalloonTip(
            4000,
            title,
            message,
            result.IsSuccess ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
    }

    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_mainViewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.CloseButtonBehavior))
        {
            UpdateTrayIconVisibility(_mainViewModel.CloseButtonBehavior);
        }
        else if (e.PropertyName is nameof(MainViewModel.CurrentModeId) or nameof(MainViewModel.IsBusy))
        {
            UpdateTrayModeState();
        }
    }

    private void UpdateTrayModeState()
    {
        if (_mainViewModel is null)
        {
            return;
        }

        foreach (var (modeId, item) in _trayModeItems)
        {
            item.Checked = string.Equals(
                modeId,
                _mainViewModel.CurrentModeId,
                StringComparison.OrdinalIgnoreCase);
            item.Enabled = !_mainViewModel.IsBusy;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Text = _mainViewModel.CurrentModeId is null
                ? "PC Mode Switcher"
                : $"PC Mode Switcher - {_mainViewModel.CurrentModeName}";
        }
    }

    internal static bool IsStartupLaunch(IEnumerable<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
}
