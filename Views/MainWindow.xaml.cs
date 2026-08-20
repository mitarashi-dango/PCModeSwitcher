using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using PCModeSwitcher.Models;
using PCModeSwitcher.ViewModels;
using PCModeSwitcher.Services;

namespace PCModeSwitcher.Views;

public partial class MainWindow : Window
{
    private const string ModeCardDataFormat = "PCModeSwitcher.VisibleModeId";

    private bool _allowClose;
    private readonly DispatcherTimer _microphoneStateTimer;
    private System.Windows.Point _modeDragStartPoint;
    private System.Windows.Point _modeDragGrabPoint;
    private string? _draggedModeId;
    private FrameworkElement? _draggedModeCard;
    private FrameworkElement? _modeDragPreview;

    public event EventHandler? HiddenToTray;

    public MainWindow()
    {
        InitializeComponent();
        _microphoneStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _microphoneStateTimer.Tick += MicrophoneStateTimer_Tick;
        _microphoneStateTimer.Start();
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

    protected override void OnClosed(EventArgs e)
    {
        _microphoneStateTimer.Stop();
        _microphoneStateTimer.Tick -= MicrophoneStateTimer_Tick;
        base.OnClosed(e);
    }

    private void MicrophoneStateTimer_Tick(object? sender, EventArgs e)
    {
        if (IsVisible && IsActive && DataContext is MainViewModel viewModel)
            viewModel.RefreshMicrophoneState();
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
            viewModel.ShowMicrophoneControls,
            viewModel.CheckForUpdatesAutomatically,
            viewModel.Hotkeys,
            viewModel.AllProfiles,
            viewModel.VisibleModeIds,
            viewModel.RestoreHotkey,
            viewModel.Language,
            this);
        if (settingsWindow.ShowDialog() != true)
        {
            return;
        }

        var result = await viewModel.SetAppPreferencesAsync(
            settingsWindow.SelectedBehavior,
            settingsWindow.ShowTrayNotification,
            settingsWindow.StartWithWindows,
            settingsWindow.Hotkeys,
            settingsWindow.SelectedVisibleModeIds,
            settingsWindow.ShowMicrophoneControls,
            settingsWindow.RestoreHotkey,
            settingsWindow.SelectedEnabledModeIds,
            settingsWindow.DeletedModeIds,
            settingsWindow.SelectedLanguage,
            settingsWindow.CheckForUpdatesAutomatically);
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                result.UserMessage,
                "PC Mode Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void OpenSettings() => Settings_Click(this, new RoutedEventArgs());

    private async void NewMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var mode = viewModel.CreateNewMode();
        var editor = new AdvancedModeEditorWindow(
            mode,
            viewModel.PowerPlans.ToList(),
            viewModel.Modes.FirstOrDefault()?.HasBattery ?? false,
            this);
        if (editor.ShowDialog() != true || editor.EditedMode is null) return;
        await ShowOperationAsync(await viewModel.AddModeAsync(editor.EditedMode));
    }

    private async void DuplicateMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { Tag: string id })
            await ShowOperationAsync(await viewModel.DuplicateModeAsync(id));
    }

    private void ModeDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _modeDragStartPoint = e.GetPosition(this);
        _draggedModeId = sender is FrameworkElement { Tag: string modeId } ? modeId : null;
        _draggedModeCard = FindModeCardContainer(sender as DependencyObject);
        _modeDragGrabPoint = _draggedModeCard is null
            ? default
            : e.GetPosition(_draggedModeCard);
    }

    private void ModeDragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _draggedModeId is null ||
            _draggedModeCard is null)
        {
            _draggedModeId = null;
            _draggedModeCard = null;
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _modeDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _modeDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var modeId = _draggedModeId;
        var draggedCard = _draggedModeCard;
        _draggedModeId = null;
        _draggedModeCard = null;
        var data = new DataObject(ModeCardDataFormat, modeId);
        var originalOpacity = draggedCard.Opacity;
        ShowModeDragPreview(draggedCard, e.GetPosition(DragPreviewLayer));
        draggedCard.Opacity = 0.12;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        finally
        {
            draggedCard.Opacity = originalOpacity;
            HideModeDragPreview();
        }
        e.Handled = true;
    }

    private void ModeDragHandle_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        UpdateModeDragPreview(Mouse.GetPosition(DragPreviewLayer));
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private void ModeCards_DragOver(object sender, DragEventArgs e)
    {
        UpdateModeDragPreview(e.GetPosition(DragPreviewLayer));
        var draggedModeId = e.Data.GetDataPresent(ModeCardDataFormat)
            ? e.Data.GetData(ModeCardDataFormat) as string
            : null;
        var target = FindModeCardContainer(e.OriginalSource as DependencyObject);
        e.Effects = draggedModeId is not null &&
                    target?.DataContext is ModeCardViewModel targetCard &&
                    !string.Equals(draggedModeId, targetCard.Mode.Id, StringComparison.OrdinalIgnoreCase)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ModeCards_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            !e.Data.GetDataPresent(ModeCardDataFormat) ||
            e.Data.GetData(ModeCardDataFormat) is not string draggedModeId)
        {
            return;
        }

        var target = FindModeCardContainer(e.OriginalSource as DependencyObject);
        if (target?.DataContext is not ModeCardViewModel targetCard ||
            string.Equals(draggedModeId, targetCard.Mode.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var insertAfter = e.GetPosition(target).X >= target.ActualWidth / 2;
        await ShowOperationAsync(await viewModel.ReorderVisibleModeAsync(
            draggedModeId,
            targetCard.Mode.Id,
            insertAfter));
        e.Handled = true;
    }

    private static FrameworkElement? FindModeCardContainer(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Name: "ModeCardRoot" } card)
                return card;
        }

        return null;
    }

    private void ShowModeDragPreview(FrameworkElement draggedCard, System.Windows.Point position)
    {
        var cardClone = new ContentControl
        {
            Content = draggedCard.DataContext,
            ContentTemplate = ModeCards.ItemTemplate,
            Width = draggedCard.ActualWidth,
            Height = draggedCard.ActualHeight,
            Opacity = 0.68,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        var previewChrome = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 37, 99, 235)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            Background = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                Direction = 270,
                ShadowDepth = 8,
                Opacity = 0.35,
                Color = System.Windows.Media.Color.FromRgb(15, 23, 42)
            },
            Child = cardClone
        };

        _modeDragPreview = previewChrome;
        DragPreviewLayer.Children.Add(previewChrome);
        UpdateModeDragPreview(position);
    }

    private void UpdateModeDragPreview(System.Windows.Point position)
    {
        if (_modeDragPreview is null)
            return;

        Canvas.SetLeft(_modeDragPreview, position.X - _modeDragGrabPoint.X);
        Canvas.SetTop(_modeDragPreview, position.Y - _modeDragGrabPoint.Y);
    }

    private void HideModeDragPreview()
    {
        if (_modeDragPreview is not null)
            DragPreviewLayer.Children.Remove(_modeDragPreview);
        _modeDragPreview = null;
    }
    private async void HideMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not FrameworkElement { Tag: string id }) return;
        if (MessageBox.Show(
                LocalizationService.Get("Dialog.HideMode"),
                "PC Mode Switcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ShowOperationAsync(await viewModel.HideModeAsync(id));
    }
    private async void ImportModes_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = LocalizationService.Get("File.SettingsFilter"),
            CheckFileExists = true,
            InitialDirectory = GetProfileDialogInitialDirectory()
        };
        if (dialog.ShowDialog(this) == true)
            await ShowOperationAsync(await viewModel.ImportProfilesAsync(dialog.FileName));
    }

    private async void ExportModes_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = LocalizationService.Get("File.SettingsFilter"),
            FileName = $"PCModeSwitcher-profiles-{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = ".json",
            InitialDirectory = GetProfileDialogInitialDirectory()
        };
        if (dialog.ShowDialog(this) == true)
            await ShowOperationAsync(await viewModel.ExportProfilesAsync(dialog.FileName));
    }

    internal static string GetProfileDialogInitialDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents))
            return documents;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(userProfile) ? userProfile : AppContext.BaseDirectory;
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var window = new DiagnosticsWindow { Owner = this };
        window.ShowDialog();
    }

    private void Support_Click(object sender, RoutedEventArgs e)
    {
        var window = new SupportWindow { Owner = this };
        window.ShowDialog();
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var menuItem = sender as MenuItem;
        if (menuItem is not null)
            menuItem.IsEnabled = false;
        try
        {
            var result = await viewModel.CheckForUpdatesAsync();
            if (!result.IsSuccess || result.Value is null)
            {
                MessageBox.Show(
                    result.UserMessage,
                    LocalizationService.Get("Update.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!result.Value.IsNewer)
            {
                MessageBox.Show(
                    LocalizationService.Format("Update.UpToDate", viewModel.AppVersion),
                    LocalizationService.Get("Update.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                    LocalizationService.Format("Update.ManualAvailable", result.Value.DisplayVersion),
                    LocalizationService.Get("Update.Title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information,
                    MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                ShowExternalLinkResult(viewModel.OpenUpdateReleasePage(result.Value));
            }
        }
        finally
        {
            if (menuItem is not null)
                menuItem.IsEnabled = true;
        }
    }

    private void ViewUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            ShowExternalLinkResult(viewModel.OpenUpdateReleasePage());
    }

    private async void DismissUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.DismissAvailableUpdateAsync();
    }

    private static void ShowExternalLinkResult(OperationResult result)
    {
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                result.UserMessage,
                "PC Mode Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow { Owner = this };
        window.ShowDialog();
    }

    private void DryRun_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { Tag: ModeCardViewModel card })
            MessageBox.Show(
                DiagnosticsService.CreateDryRun(card.Mode, viewModel.PowerPlans),
                LocalizationService.Get("Dialog.ReviewTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
    }

    private Task ShowOperationAsync(OperationResult result)
    {
        if (!result.IsSuccess)
            MessageBox.Show(result.UserMessage, "PC Mode Switcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

}
