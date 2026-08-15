using System.Reflection;
using System.Windows;
using PCModeSwitcher.Services;

namespace PCModeSwitcher.Views;

public partial class AboutWindow : Window
{
    private const string ProjectUrl = "https://github.com/mitarashi-dango/PCModeSwitcher";

    public AboutWindow()
    {
        InitializeComponent();

        var assembly = typeof(AboutWindow).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? LocalizationService.Get("Common.Unknown");
        VersionText.Text = LocalizationService.Format("About.Version", version);
    }

    private void ProjectLink_Click(object sender, RoutedEventArgs e)
    {
        var result = ExternalLinkService.Open(new Uri(ProjectUrl));
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
