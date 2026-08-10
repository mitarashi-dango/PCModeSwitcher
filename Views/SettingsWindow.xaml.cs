using System.Windows;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Views;

public partial class SettingsWindow : Window
{
    public CloseButtonBehavior SelectedBehavior { get; private set; }
    public bool ShowTrayNotification { get; private set; }

    public SettingsWindow(
        CloseButtonBehavior currentBehavior,
        bool showTrayNotification,
        Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SelectedBehavior = currentBehavior;
        ShowTrayNotification = showTrayNotification;
        MinimizeToTrayOption.IsChecked = currentBehavior == CloseButtonBehavior.MinimizeToTray;
        ExitApplicationOption.IsChecked = currentBehavior == CloseButtonBehavior.ExitApplication;
        ShowTrayNotificationOption.IsChecked = showTrayNotification;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SelectedBehavior = MinimizeToTrayOption.IsChecked == true
            ? CloseButtonBehavior.MinimizeToTray
            : CloseButtonBehavior.ExitApplication;
        ShowTrayNotification = ShowTrayNotificationOption.IsChecked == true;
        DialogResult = true;
    }
}
