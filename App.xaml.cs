using PCModeSwitcher.Services;
using PCModeSwitcher.Models;
using PCModeSwitcher.ViewModels;
using PCModeSwitcher.Views;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Activation;
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
    private ModeEngine? _modeEngine;
    private UpdateCheckService? _updateCheckService;
    private CancellationTokenSource? _updateCheckScheduleCancellation;
    private readonly AppLogger _appLogger = new();
    private bool _trayHintShown;
    private bool _isWindowHiddenToTray;
    private bool _isUnhandledExceptionDialogOpen;
    private bool _isUpdateBalloonActive;
    private Forms.ToolStripMenuItem? _restoreTrayItem;

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

        LocalizationService.LanguageChanged += OnLanguageChanged;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var settingsService = new SettingsService();
        var powerService = new PowerSettingsService();
        var microphoneMuteService = new MicrophoneMuteService();
        var startupService = new StartupService();
        _globalHotkeyService = new GlobalHotkeyService();
        _modeEngine = new ModeEngine();
        _updateCheckService = new UpdateCheckService();
        var viewModel = new MainViewModel(
            settingsService,
            powerService,
            microphoneMuteService,
            startupService,
            _globalHotkeyService,
            _modeEngine,
            _updateCheckService);
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
        await HandleIncompleteSessionAsync(viewModel);
        CreateTrayModeItems();
        viewModel.Modes.CollectionChanged += OnModesCollectionChanged;
        UpdateTrayModeState();
        UpdateTrayIconVisibility(viewModel.CloseButtonBehavior);
        RestartAutomaticUpdateSchedule(TimeSpan.FromSeconds(30));
    }

    protected override void OnExit(Wpf.ExitEventArgs e)
    {
        _updateCheckScheduleCancellation?.Cancel();
        _updateCheckScheduleCancellation?.Dispose();
        _updateCheckScheduleCancellation = null;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        foreach (var item in _trayModeItems.Values)
        {
            item.Image?.Dispose();
            item.Image = null;
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

        _modeEngine?.Dispose();
        _modeEngine = null;

        _updateCheckService?.Dispose();
        _updateCheckService = null;

        if (_mainViewModel is not null)
        {
            _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
            _mainViewModel.Modes.CollectionChanged -= OnModesCollectionChanged;
            foreach (var mode in _mainViewModel.Modes)
            {
                mode.PropertyChanged -= OnTrayModePropertyChanged;
            }
        }
        _mainViewModel = null;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_singleInstanceCoordinator is not null)
        {
            _singleInstanceCoordinator.ActivationRequested -= OnActivationRequested;
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true;
        _appLogger.WriteUnhandledException(args.Exception);
        if (_isUnhandledExceptionDialogOpen)
        {
            return;
        }

        _isUnhandledExceptionDialogOpen = true;
        try
        {
            Wpf.MessageBox.Show(
                LocalizationService.Translate("予期しない問題が発生しました。操作を中止しました。"),
                "PC Mode Switcher",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Error);
        }
        finally
        {
            _isUnhandledExceptionDialogOpen = false;
        }
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new Forms.ContextMenuStrip
        {
            ShowItemToolTips = true,
            ShowCheckMargin = true,
            ShowImageMargin = true
        };
        var loadingItem = new Forms.ToolStripMenuItem(LocalizationService.Get("Tray.Loading"))
        {
            Enabled = false,
            Tag = "loading"
        };
        var showItem = new Forms.ToolStripMenuItem(LocalizationService.Get("Tray.Show")) { Tag = "show" };
        showItem.Click += (_, _) => Dispatcher.Invoke(RestoreMainWindow);

        _restoreTrayItem = new Forms.ToolStripMenuItem(LocalizationService.Get("Common.Restore")) { Enabled = false, Tag = "restore" };
        _restoreTrayItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_mainViewModel is not null) await _mainViewModel.RestoreModeAsync();
        }));

        var settingsItem = new Forms.ToolStripMenuItem(LocalizationService.Get("Tray.OpenSettings")) { Tag = "settings" };
        settingsItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            RestoreMainWindow();
            _mainWindow?.OpenSettings();
        }));

        var exitItem = new Forms.ToolStripMenuItem(LocalizationService.Get("Tray.Exit")) { Tag = "exit" };
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication));

        _trayMenu.Items.Add(loadingItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator { Tag = "mode-separator" });
        _trayMenu.Items.Add(showItem);
        _trayMenu.Items.Add(_restoreTrayItem);
        _trayMenu.Items.Add(settingsItem);
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
        _trayIcon.BalloonTipClicked += (_, _) =>
        {
            if (_isUpdateBalloonActive)
                Dispatcher.Invoke(RestoreMainWindow);
        };
        _trayIcon.BalloonTipClosed += (_, _) => _isUpdateBalloonActive = false;
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

        var modesById = _mainViewModel.Modes.ToDictionary(
            mode => mode.Mode.Id,
            StringComparer.OrdinalIgnoreCase);
        var modeOrder = BuildTrayModeOrder(
            _mainViewModel.VisibleModes.Select(mode => mode.Mode.Id),
            _mainViewModel.Modes.Select(mode => mode.Mode.Id));
        var insertIndex = 0;
        foreach (var modeId in modeOrder)
        {
            var mode = modesById[modeId];
            var modeIcon = LoadTrayModeImage(modeId, mode.Icon) ?? RenderTrayModeIcon(mode.Icon);
            var modeItem = new Forms.ToolStripMenuItem(mode.Name)
            {
                CheckOnClick = false,
                Image = modeIcon,
                ImageScaling = Forms.ToolStripItemImageScaling.SizeToFit,
                Tag = modeId,
                ToolTipText = mode.TrayToolTipText
            };
            modeItem.Click += (_, _) => Dispatcher.BeginInvoke(
                new Action(async () => await ApplyModeFromTrayAsync(modeId)));
            _trayMenu.Items.Insert(insertIndex++, modeItem);
            _trayModeItems[modeId] = modeItem;
            mode.PropertyChanged += OnTrayModePropertyChanged;
        }
    }

    private static System.Drawing.Image? LoadTrayModeImage(string modeId, string icon)
    {
        var source = ModeIconAssets.GetCustomIconSource(modeId, icon);
        if (source is null)
        {
            return null;
        }

        var resource = Wpf.Application.GetResourceStream(new Uri(source, UriKind.Relative));
        if (resource is null)
        {
            return null;
        }

        using var stream = resource.Stream;
        using var image = System.Drawing.Image.FromStream(stream);
        return new System.Drawing.Bitmap(image);
    }

    private static System.Drawing.Image RenderTrayModeIcon(string icon)
    {
        const int canvasSize = 32;
        var bitmap = new System.Drawing.Bitmap(
            canvasSize,
            canvasSize,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        using var font = new System.Drawing.Font(
            "Segoe UI Emoji",
            21f,
            System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Pixel);
        Forms.TextRenderer.DrawText(
            graphics,
            icon,
            font,
            new System.Drawing.Rectangle(0, 0, canvasSize, canvasSize),
            System.Drawing.Color.Black,
            Forms.TextFormatFlags.HorizontalCenter |
            Forms.TextFormatFlags.VerticalCenter |
            Forms.TextFormatFlags.NoPadding |
            Forms.TextFormatFlags.NoPrefix);
        return bitmap;
    }

    private void OnTrayModePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ModeCardViewModel mode ||
            e.PropertyName != nameof(ModeCardViewModel.TrayToolTipText) ||
            !_trayModeItems.TryGetValue(mode.Mode.Id, out var item))
        {
            return;
        }

        item.ToolTipText = mode.TrayToolTipText;
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
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (string.Equals(e.ModeId, "restore", StringComparison.OrdinalIgnoreCase))
                await _mainViewModel?.RestoreModeAsync()!;
            else
                await ApplyModeFromTrayAsync(e.ModeId);
        }));
    }

    private static async Task HandleIncompleteSessionAsync(MainViewModel viewModel)
    {
        var incomplete = await viewModel.GetIncompleteSessionAsync();
        if (!incomplete.IsSuccess)
        {
            Wpf.MessageBox.Show(incomplete.UserMessage, "PC Mode Switcher",
                Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
            return;
        }
        if (ShouldForgetRestoreOnStartup(incomplete.Value))
        {
            viewModel.IgnoreIncompleteSession();
            return;
        }
        if (!NeedsAutomaticRecovery(incomplete.Value)) return;

        var recovery = await viewModel.RestoreModeAsync();
        if (recovery is { IsSuccess: false })
        {
            Wpf.MessageBox.Show(
                LocalizationService.Format("Dialog.AutomaticRecoveryFailed", viewModel.StatusMessage),
                "PC Mode Switcher",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Warning);
        }
    }

    internal static bool NeedsAutomaticRecovery(ModeSessionSnapshot? session) =>
        session?.IsApplying == true;

    internal static bool ShouldForgetRestoreOnStartup(ModeSessionSnapshot? session) =>
        session is { IsApplying: false };

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

    private async void OnWindowHiddenToTray(object? sender, EventArgs e)
    {
        _isWindowHiddenToTray = true;
        if (_mainViewModel is not null)
        {
            UpdateTrayIconVisibility(_mainViewModel.CloseButtonBehavior);
        }

        if (await TryShowUpdateTrayNotificationAsync())
            return;

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
            LocalizationService.Get("Tray.MinimizedHint"),
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
            ? LocalizationService.Format("Status.ModeApplied", mode.Name)
            : result.Steps.Any(step => step.IsSuccess && !step.IsSkipped)
                ? LocalizationService.Format("Status.ModePartiallyApplied", mode.Name)
                : LocalizationService.Format("Status.ModeFailed", mode.Name);
        var message = result.IsSuccess
            ? LocalizationService.Get("Tray.AppliedSettings")
            : string.Join("\n", result.Steps.Select(step =>
                $"{(step.IsSkipped ? "–" : step.IsSuccess ? "✓" : "×")} {LocalizationService.Translate(step.DisplayName ?? step.Name)}"));
        _trayIcon.ShowBalloonTip(
            4000,
            title,
            message,
            result.IsSuccess ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_trayMenu is null)
            return;
        foreach (Forms.ToolStripItem item in _trayMenu.Items)
        {
            item.Text = item.Tag switch
            {
                "loading" => LocalizationService.Get("Tray.Loading"),
                "show" => LocalizationService.Get("Tray.Show"),
                "restore" => LocalizationService.Get("Common.Restore"),
                "settings" => LocalizationService.Get("Tray.OpenSettings"),
                "exit" => LocalizationService.Get("Tray.Exit"),
                _ => item.Text
            };
        }
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
        else if (e.PropertyName == nameof(MainViewModel.HasActiveSession))
        {
            if (_restoreTrayItem is not null)
                _restoreTrayItem.Enabled = _mainViewModel.HasActiveSession && !_mainViewModel.IsBusy;
        }
        else if (e.PropertyName == nameof(MainViewModel.VisibleModeIds))
        {
            RebuildTrayModeItems();
        }
        else if (e.PropertyName == nameof(MainViewModel.CheckForUpdatesAutomatically))
        {
            RestartAutomaticUpdateSchedule(TimeSpan.Zero);
        }
    }

    private void RestartAutomaticUpdateSchedule(TimeSpan initialDelay)
    {
        _updateCheckScheduleCancellation?.Cancel();
        _updateCheckScheduleCancellation?.Dispose();
        _updateCheckScheduleCancellation = null;
        if (_mainViewModel?.CheckForUpdatesAutomatically != true)
            return;

        _updateCheckScheduleCancellation = new CancellationTokenSource();
        _ = RunAutomaticUpdateChecksAsync(
            initialDelay,
            _updateCheckScheduleCancellation.Token);
    }

    private async Task RunAutomaticUpdateChecksAsync(
        TimeSpan initialDelay,
        CancellationToken cancellationToken)
    {
        try
        {
            if (initialDelay > TimeSpan.Zero)
                await Task.Delay(initialDelay, cancellationToken);

            while (_mainViewModel is { } viewModel)
            {
                var delay = viewModel.GetAutomaticUpdateCheckDelay(DateTimeOffset.UtcNow);
                if (delay is null)
                    return;
                if (delay.Value > TimeSpan.Zero)
                    await Task.Delay(delay.Value, cancellationToken);

                var result = await viewModel.CheckForUpdatesAsync(cancellationToken);
                if (result.IsSuccess && result.Value?.IsNewer == true)
                    _ = await TryShowUpdateTrayNotificationAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> TryShowUpdateTrayNotificationAsync(
        CancellationToken cancellationToken = default)
    {
        if (_mainViewModel is not { AvailableUpdate: { } update } viewModel ||
            (!_isWindowHiddenToTray && _mainWindow?.IsVisible == true) ||
            _trayIcon is null ||
            !_trayIcon.Visible ||
            !await viewModel.TryMarkUpdateNotificationShownAsync(cancellationToken))
        {
            return false;
        }

        _isUpdateBalloonActive = true;
        _trayIcon.ShowBalloonTip(
            5000,
            LocalizationService.Get("Update.NotificationTitle"),
            LocalizationService.Format("Update.Available", update.DisplayVersion),
            Forms.ToolTipIcon.Info);
        return true;
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
        if (_restoreTrayItem is not null)
            _restoreTrayItem.Enabled = _mainViewModel.HasActiveSession && !_mainViewModel.IsBusy;
    }

    private void OnModesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RebuildTrayModeItems();
    }

    private void RebuildTrayModeItems()
    {
        foreach (var item in _trayModeItems.Values)
        {
            if (item.Tag is string id)
            {
                var oldMode = _mainViewModel?.Modes.FirstOrDefault(mode => mode.Mode.Id == id);
                if (oldMode is not null) oldMode.PropertyChanged -= OnTrayModePropertyChanged;
            }
            _trayMenu?.Items.Remove(item);
            item.Image?.Dispose();
            item.Image = null;
            item.Dispose();
        }
        _trayModeItems.Clear();
        CreateTrayModeItems();
        UpdateTrayModeState();
    }

    internal static IReadOnlyList<string> BuildTrayModeOrder(
        IEnumerable<string> visibleModeIds,
        IEnumerable<string> allModeIds)
    {
        var availableModeIds = allModeIds.ToList();
        var availableModeIdSet = availableModeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return visibleModeIds
            .Where(availableModeIdSet.Contains)
            .Concat(availableModeIds)
            .Where(seen.Add)
            .ToList();
    }

    internal static bool IsStartupLaunch(IEnumerable<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)) ||
        IsPackagedStartupActivation();

    private static bool IsPackagedStartupActivation()
    {
        try
        {
            return global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs()
                is IStartupTaskActivatedEventArgs;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            return false;
        }
    }
}
