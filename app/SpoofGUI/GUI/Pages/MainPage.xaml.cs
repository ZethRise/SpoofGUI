using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SpoofGUI.GUI.ViewModels;

namespace SpoofGUI.GUI.Pages;

public sealed partial class MainPage : Page
{
    private readonly MainPageViewModel _vm;

    public MainPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<MainPageViewModel>();
        Loaded += async (_, _) => await _vm.LoadAsync(this);
    }

    public void RenderIdle(string profileName, string flow, string sni)
    {
        HeadlineText.Text = "SpoofGUI";
        HeadlineSub.Text  = "Connect and use your X-Ray Client.";
        ProfileName.Text  = profileName;
        ProfileFlow.Text  = flow;
        ProfileSni.Text   = $"SNI: {sni}";
        StatUptime.Text = StatConns.Text = StatIface.Text = "—";
        ConnectButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
    }

    public void RenderConnecting()
    {
        HeadlineText.Text = "starting";
        HeadlineSub.Text  = "Starting local listener and attaching WinDivert.";
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
    }

    public void RenderLive(string iface, ulong uptimeMs, uint conns)
    {
        HeadlineText.Text = "ready";
        HeadlineSub.Text  = $"Connect and use your X-Ray Client. {conns} active connection{(conns == 1 ? "" : "s")}.";
        StatUptime.Text = FormatUptime(uptimeMs);
        StatConns.Text  = conns.ToString();
        StatIface.Text  = iface;
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
    }

    public void RenderError(string message)
    {
        HeadlineText.Text = "error";
        HeadlineSub.Text  = message;
        ConnectButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
    }

    private static string FormatUptime(ulong ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}h {t.Minutes:D2}m"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    private async void OnConnect(object sender, object e) => await _vm.ConnectAsync();
    private async void OnDisconnect(object sender, object e) => await _vm.DisconnectAsync();
    private void OnEditProfile(object sender, object e) => Frame.Navigate(typeof(ConfigPage));
}
