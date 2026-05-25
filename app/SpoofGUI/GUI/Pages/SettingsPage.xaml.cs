using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpoofGUI.GUI.ViewModels;

namespace SpoofGUI.GUI.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsPageViewModel _vm;
    private bool _initializing = true;

    public SettingsPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<SettingsPageViewModel>();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        UpdateVersion.Text = $"installed: {_vm.AppVersion}";
        UpdateLastCheck.Text = _vm.LastUpdateCheckText();
        ThemeChoice.SelectedIndex = _vm.Theme == "light" ? 1 : 0;
        _initializing = false;
    }

    private async void OnCheckUpdates(object sender, object e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateLastCheck.Text = "checking...";
        UpdateReleaseLink.Visibility = Visibility.Collapsed;

        var res = await _vm.CheckForUpdatesAsync();
        UpdateVersion.Text = res.StatusText;
        UpdateLastCheck.Text = res.LastCheckText;
        UpdateReleaseLink.NavigateUri = new Uri(res.ReleaseUrl);
        UpdateReleaseLink.Content = res.IsUpdateAvailable ? "open new release" : "open latest release";
        UpdateReleaseLink.Visibility = Visibility.Visible;
        CheckUpdatesButton.IsEnabled = true;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var theme = ThemeChoice.SelectedIndex == 1 ? "light" : "dark";
        _vm.SetTheme(theme);
        App.CurrentWindow?.ApplyTheme(theme);
    }
}
