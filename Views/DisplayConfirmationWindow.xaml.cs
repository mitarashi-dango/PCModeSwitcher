using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using PCModeSwitcher.Services;

namespace PCModeSwitcher.Views;

public partial class DisplayConfirmationWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly uint _rate;
    private int _remaining = 15;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Message => LocalizationService.Format("DisplayConfirmation.Message", _rate, _remaining);
    public DisplayConfirmationWindow(uint rate)
    {
        InitializeComponent(); _rate = rate; DataContext = this;
        _timer.Tick += Timer_Tick; _timer.Start();
    }
    private void Timer_Tick(object? sender, EventArgs e) { _remaining--; PropertyChanged?.Invoke(this, new(nameof(Message))); if (_remaining <= 0) { _timer.Stop(); DialogResult = false; } }
    private void Accept_Click(object sender, RoutedEventArgs e) { _timer.Stop(); DialogResult = true; }
    private void Revert_Click(object sender, RoutedEventArgs e) { _timer.Stop(); DialogResult = false; }
    protected override void OnClosing(CancelEventArgs e) { _timer.Stop(); base.OnClosing(e); }
}
