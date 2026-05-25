using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpoofGUI.Core;
using Windows.ApplicationModel.DataTransfer;

namespace SpoofGUI.GUI.Pages;

public sealed partial class LogsPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public LogsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _timer.Tick += (_, _) => Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Refresh();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        AppLog.Clear();
        Refresh();
    }

    private void OnCopyLogs(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, AppLog.Snapshot()));
        Clipboard.SetContent(package);
    }

    private void Refresh() => LogText.Text = string.Join(Environment.NewLine, AppLog.Snapshot());
}
