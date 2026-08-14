using System.Windows;
using Microsoft.Win32;
using PCModeSwitcher.Services;

namespace PCModeSwitcher.Views;

public partial class DiagnosticsWindow : Window
{
    private readonly DiagnosticsService _service = new();
    public DiagnosticsWindow() { InitializeComponent(); Loaded += async (_, _) => ReportText.Text = await _service.CreateReportAsync(); }
    private void Copy_Click(object sender, RoutedEventArgs e) { Clipboard.SetText(ReportText.Text); }
    private void Logs_Click(object sender, RoutedEventArgs e) => _service.OpenLogs();
    private void Settings_Click(object sender, RoutedEventArgs e) => _service.OpenSettings();
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter=LocalizationService.Get("File.TextFilter"), FileName=$"PCModeSwitcher-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dialog.ShowDialog(this) == true) { var result = await _service.SaveReportAsync(dialog.FileName); if (!result.IsSuccess) MessageBox.Show(result.UserMessage); }
    }
    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var result = _service.BackupAllSettings(); MessageBox.Show(result.UserMessage, "PC Mode Switcher", MessageBoxButton.OK, result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
