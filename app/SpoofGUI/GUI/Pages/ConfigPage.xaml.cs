using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SpoofGUI.GUI.ViewModels;

namespace SpoofGUI.GUI.Pages;

public sealed partial class ConfigPage : Page
{
    private readonly ConfigPageViewModel _vm;

    public ConfigPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ConfigPageViewModel>();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        var p = _vm.LoadProfile();
        ListenHost.Text  = p.ListenHost;
        ListenPort.Text  = p.ListenPort.ToString();
        ConnectIp.Text   = p.ConnectIp;
        ConnectPort.Text = p.ConnectPort.ToString();
        FakeSni.Text     = p.FakeSni;
    }

    private void OnRevert(object sender, object e) => Load();

    private void OnSave(object sender, object e)
    {
        if (!int.TryParse(ListenPort.Text, out var lp) || lp <= 0 || lp > 65535) return;
        if (!int.TryParse(ConnectPort.Text, out var cp) || cp <= 0 || cp > 65535) return;

        var p = _vm.LoadProfile();
        p.ListenHost  = ListenHost.Text.Trim();
        p.ListenPort  = lp;
        p.ConnectIp   = ConnectIp.Text.Trim();
        p.ConnectPort = cp;
        p.FakeSni     = FakeSni.Text.Trim();
        _vm.Save(p);
    }
}
