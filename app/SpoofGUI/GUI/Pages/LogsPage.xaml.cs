using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpoofGUI.Core;
using Windows.ApplicationModel.DataTransfer;

namespace SpoofGUI.GUI.Pages;

public sealed partial class LogsPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private ScrollViewer? _logScroll;

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

    private void Refresh()
    {
        _logScroll ??= FindScrollViewer(LogText);
        var atBottom = _logScroll is null || _logScroll.VerticalOffset >= _logScroll.ScrollableHeight - 8;

        LogText.Text = string.Join(Environment.NewLine, AppLog.Snapshot());

        if (atBottom && _logScroll is not null)
        {
            LogText.UpdateLayout();
            _logScroll.ChangeView(null, _logScroll.ScrollableHeight, null, true);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is ScrollViewer found) return found;
        }

        return null;
    }
}
