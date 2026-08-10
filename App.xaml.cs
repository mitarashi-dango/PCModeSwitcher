using System.Windows;
using PCModeSwitcher.Services;
using PCModeSwitcher.ViewModels;
using PCModeSwitcher.Views;

namespace PCModeSwitcher;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "予期しない問題が発生しました。操作を中止しました。",
                "PC Mode Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var settingsService = new SettingsService();
        var powerService = new PowerSettingsService();
        var viewModel = new MainViewModel(settingsService, powerService);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
    }
}
