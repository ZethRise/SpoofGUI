using Microsoft.UI.Xaml.Controls;
using SpoofGUI.GUI.Pages;

namespace SpoofGUI.GUI;

public sealed partial class Shell : UserControl
{
    public Shell()
    {
        InitializeComponent();
        Loaded += (_, _) => Navigate(typeof(MainPage));
    }

    private void Navigate(Type page) => ContentFrame.Navigate(page);

    private void OnNavMain(object sender, object e)     => Navigate(typeof(MainPage));
    private void OnNavConfig(object sender, object e)   => Navigate(typeof(ConfigPage));
    private void OnNavV2Ray(object sender, object e)    => Navigate(typeof(V2RayPage));
    private void OnNavSettings(object sender, object e) => Navigate(typeof(SettingsPage));
    private void OnNavLogs(object sender, object e)     => Navigate(typeof(LogsPage));

    public void SetStatus(string line) { }
}
