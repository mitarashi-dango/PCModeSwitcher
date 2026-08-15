using System.Windows;
using PCModeSwitcher.Services;

namespace PCModeSwitcher.Views;

public partial class SupportWindow : Window
{
    private readonly Uri? _koFiUri;

    public SupportWindow()
    {
        InitializeComponent();
        SupportLinks.TryCreateSupportUri(SupportLinks.KoFi, out _koFiUri);
        KoFiButton.IsEnabled = _koFiUri is not null;
    }

    private void KoFiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_koFiUri is null)
        {
            return;
        }

        var result = ExternalLinkService.Open(_koFiUri);
        if (result.IsSuccess)
        {
            Close();
        }
        else
        {
            MessageBox.Show(
                result.UserMessage,
                "PC Mode Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
