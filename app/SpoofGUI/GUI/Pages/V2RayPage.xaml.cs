using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpoofGUI.Core;
using SpoofGUI.GUI.ViewModels;
using SpoofGUI.Models;
using Windows.ApplicationModel.DataTransfer;

namespace SpoofGUI.GUI.Pages;

public sealed partial class V2RayPage : Page
{
    private readonly V2RayPageViewModel _vm;
    private V2RayProfile? _selected;
    private bool _ready;
    private bool _pinging;
    private bool _xrayRunning;
    private bool _systemProxyActive;
    private bool _tunnelActive;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly NetStats.BandwidthSampler _sampler = new();
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime? _connectedAt;

    public V2RayPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<V2RayPageViewModel>();
        Loaded += async (_, _) => await LoadAsync();
        Unloaded += (_, _) => _statsTimer.Stop();
        _statsTimer.Tick += (_, _) => UpdateStats();
        _ready = true;
    }

    private void UpdateStats()
    {
        if (!_xrayRunning)
        {
            StatStatus.Text = "idle";
            StatUptime.Text = "—";
            StatDown.Text = "0 B/s";
            StatUp.Text = "0 B/s";
            StatTotal.Text = NetStats.FormatBytes(_sampler.TotalBytesRecv) + " / " + NetStats.FormatBytes(_sampler.TotalBytesSent);
            return;
        }
        _sampler.Tick();
        StatStatus.Text = _tunnelActive ? "live · tunnel" : _systemProxyActive ? "live · system proxy" : "live";
        if (_connectedAt is DateTime t)
        {
            var up = DateTime.UtcNow - t;
            StatUptime.Text = up.TotalHours >= 1
                ? $"{(int)up.TotalHours}h {up.Minutes:D2}m"
                : $"{up.Minutes:D2}:{up.Seconds:D2}";
        }
        StatDown.Text = NetStats.FormatRate(_sampler.RecvBps);
        StatUp.Text = NetStats.FormatRate(_sampler.SendBps);
        StatTotal.Text = NetStats.FormatBytes(_sampler.TotalBytesRecv) + " / " + NetStats.FormatBytes(_sampler.TotalBytesSent);
    }

    private async Task LoadAsync()
    {
        ModeSelector.SelectedIndex = _vm.V2RayModeIndex;
        try
        {
            Reload();
            CoreStatusText.Text = File.Exists(Paths.SingBoxExePath)
                ? "sing-box core: bundled"
                : "sing-box core: missing";
        }
        catch (Exception ex)
        {
            CoreStatusText.Text = "sing-box unavailable";
            StatusText.Text = ex.Message;
        }

        bool xrayUp;
        try { xrayUp = await _vm.RefreshRunningAsync(); }
        catch { xrayUp = _vm.IsRunning; }

        _tunnelActive = _vm.TunnelRunning;
        _xrayRunning = xrayUp || _tunnelActive;

        _systemProxyActive = SystemProxy.IsEnabled();
        if (_xrayRunning && _connectedAt is null) _connectedAt = DateTime.UtcNow;
        if (!_statsTimer.IsEnabled) _statsTimer.Start();
        UpdateStats();
        RenderActionState();
    }

    private static string ModeFromIndex(int idx) => idx switch
    {
        2 => "SystemProxy",
        1 => "Tunnel",
        _ => "Proxy",
    };

    private string CurrentMode() => ModeFromIndex(ModeSelector.SelectedIndex);

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _vm.SetMode(CurrentMode());
    }

    private void Reload()
    {
        var profiles = _vm.LoadProfiles();
        ProfileList.ItemsSource = profiles;
        if (_selected is not null)
        {
            ProfileList.SelectedItem = profiles.FirstOrDefault(p => p.Id == _selected.Id);
        }
    }

    private void OnImport(object sender, object e)
    {
        if (string.IsNullOrWhiteSpace(ImportText.Text)) return;

        try
        {
            var result = _vm.ImportMany(ImportText.Text, CurrentMode());
            if (result.Imported.Count == 0)
            {
                StatusText.Text = result.Failed > 0 ? $"import failed: {result.Failed} invalid config(s)" : "nothing to import";
            }
            else
            {
                _selected = result.Imported[^1];
                ImportText.Text = "";
                var summary = result.Imported.Count == 1
                    ? $"imported: {_selected.Name}"
                    : $"imported {result.Imported.Count} configs";
                if (result.Failed > 0) summary += $" ({result.Failed} skipped)";
                StatusText.Text = summary;
                Reload();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"import failed: {ex.Message}";
        }
        RenderActionState();
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        _selected = ProfileList.SelectedItem as V2RayProfile;
        RenderActionState();
    }

    private async void OnConnect(object sender, object e)
    {
        if (_selected is null)
        {
            StatusText.Text = "select a config first";
            return;
        }

        ConnectButton.IsEnabled = false;
        SetConnecting(true);

        var mode = CurrentMode();
        if (string.Equals(mode, "Tunnel", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "connecting… (sing-box tunnel)";
            try
            {
                await _vm.StartTunnelAsync(_selected);
                _xrayRunning = true;
                _tunnelActive = true;
                _sampler.Reset();
                _connectedAt = DateTime.UtcNow;
                _systemProxyActive = false;
                StatusText.Text = "connected + tunnel (sing-box routing all traffic)";
            }
            catch (Exception tx)
            {
                try { await _vm.StopTunnelAsync(); } catch { }
                _xrayRunning = false;
                _tunnelActive = false;
                StatusText.Text = $"tunnel failed: {tx.Message}";
            }
            RenderActionState();
            return;
        }

        StatusText.Text = "starting xray...";
        try
        {
            await _vm.StartAsync(_selected);
            _xrayRunning = true;
            _sampler.Reset();
            _connectedAt = DateTime.UtcNow;
            if (string.Equals(mode, "SystemProxy", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    SystemProxy.Enable(_vm.SystemProxyEndpoint);
                    _systemProxyActive = true;
                    StatusText.Text = "connected + system proxy active";
                }
                catch (Exception px)
                {
                    StatusText.Text = $"connected (proxy set failed: {px.Message})";
                }
            }
            else
            {

                if (SystemProxy.IsOurs(_vm.SystemProxyEndpoint))
                {
                    try { SystemProxy.Disable(); } catch { }
                }
                _systemProxyActive = false;
                StatusText.Text = $"connected: socks 127.0.0.1:{_vm.SocksPort}, http 127.0.0.1:{_vm.HttpPort}";
            }
        }
        catch (Exception ex)
        {
            _xrayRunning = false;
            StatusText.Text = $"connect failed: {ex.Message}";
        }
        RenderActionState();
    }

    private async void OnStop(object sender, object e)
    {
        StopButton.IsEnabled = false;
        StatusText.Text = "stopping...";

        if (_vm.TunnelRunning)
        {
            try { await _vm.StopTunnelAsync(); } catch (Exception tx) { AppLog.Warn($"tunnel stop: {tx.Message}"); }
        }
        _tunnelActive = false;

        try { await _vm.StopAsync(); }
        catch (Exception ex) { StatusText.Text = $"stop failed: {ex.Message}"; }
        _xrayRunning = false;
        _connectedAt = null;
        if (_systemProxyActive)
        {
            try { SystemProxy.Disable(); _systemProxyActive = false; } catch { }
        }
        StatusText.Text = "stopped";
        UpdateStats();
        RenderActionState();
    }

    private async void OnNew(object sender, object e)
    {
        var profile = new V2RayProfile();
        if (await ShowEditorAsync(profile))
        {
            _vm.Save(profile);
            _selected = profile;
            StatusText.Text = $"saved: {profile.Name}";
            Reload();
        }
        RenderActionState();
    }

    private async void OnEdit(object sender, object e)
    {
        if (_selected is null)
        {
            StatusText.Text = "select a config first";
            return;
        }

        var draft = Clone(_selected);
        if (await ShowEditorAsync(draft))
        {
            _vm.Save(draft);
            _selected = draft;
            StatusText.Text = $"saved: {draft.Name}";
            Reload();
        }
        RenderActionState();
    }

    private void OnDelete(object sender, object e)
    {
        if (_selected is null || _selected.Id == 0)
        {
            StatusText.Text = "select a config first";
            return;
        }

        var deleted = _selected.Name;
        _vm.Delete(_selected);
        _selected = null;
        StatusText.Text = $"deleted: {deleted}";
        Reload();
        RenderActionState();
    }

    private async Task<bool> ShowEditorAsync(V2RayProfile profile)
    {
        var name = Field("Name", profile.Name);
        var protocol = Field("Protocol", profile.Protocol);
        var address = Field("Address", profile.Address);
        var port = Field("Port", profile.Port.ToString());
        var userId = Field("UUID / password", profile.UserId);
        var security = Field("Security", profile.Security);
        var transport = Field("Transport", profile.Transport);
        var serverName = Field("SNI", profile.ServerName);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(name.Container);
        panel.Children.Add(protocol.Container);
        panel.Children.Add(address.Container);
        panel.Children.Add(port.Container);
        panel.Children.Add(userId.Container);
        panel.Children.Add(security.Container);
        panel.Children.Add(transport.Container);
        panel.Children.Add(serverName.Container);

        var dialog = new ContentDialog
        {
            Title = "Edit config",
            Content = panel,
            PrimaryButtonText = "save",
            CloseButtonText = "cancel",
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return false;
        }

        if (!int.TryParse(port.Box.Text, out var parsedPort) || parsedPort <= 0 || parsedPort > 65535)
        {
            StatusText.Text = "invalid port";
            return false;
        }

        profile.Name = name.Box.Text.Trim();
        profile.Protocol = protocol.Box.Text.Trim().ToLowerInvariant();
        profile.Address = address.Box.Text.Trim();
        profile.Port = parsedPort;
        profile.UserId = userId.Box.Text.Trim();
        profile.Security = security.Box.Text.Trim();
        profile.Transport = transport.Box.Text.Trim();
        profile.ServerName = serverName.Box.Text.Trim();
        return true;
    }

    private (StackPanel Container, TextBox Box) Field(string label, string value)
    {
        var box = new TextBox
        {
            Text = value,
            Style = (Style)Application.Current.Resources["FieldTextBox"],
        };
        var container = new StackPanel();
        container.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["FieldLabel"],
        });
        container.Children.Add(box);
        return (container, box);
    }

    private void SetConnecting(bool on)
    {
        ConnectSpinner.IsActive = on;
        ConnectSpinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ConnectContent.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderActionState()
    {
        SetConnecting(false);
        var hasSelection = _selected is not null;
        ConnectButton.IsEnabled = hasSelection && !_xrayRunning;
        StopButton.IsEnabled = _xrayRunning;
        PingButton.IsEnabled = !_pinging;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void SetPing(V2RayProfile profile, string value) =>
        _dispatcher.TryEnqueue(() => profile.Ping = value);

    private async Task PingProfileAsync(V2RayProfile profile, SemaphoreSlim gate)
    {
        await gate.WaitAsync();
        SetPing(profile, "…");
        try
        {
            var ms = await _vm.TestRealDelayAsync(profile);
            SetPing(profile, $"{ms} ms");
        }
        catch
        {
            SetPing(profile, "timeout");
        }
        finally
        {
            gate.Release();
        }
    }

    private void SetPinging(bool on)
    {
        _pinging = on;
        PingButton.IsEnabled = !on;
        PingLabel.Text = on ? "pinging…" : "ping all";
        PingIcon.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        PingSpinner.IsActive = on;
        PingSpinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) RenderActionState();
    }

    private async void OnPingAll(object sender, object e)
    {
        if (_pinging) return;
        if (ProfileList.ItemsSource is not IReadOnlyList<V2RayProfile> profiles || profiles.Count == 0)
        {
            StatusText.Text = "no configs to ping";
            return;
        }

        SetPinging(true);
        StatusText.Text = $"pinging {profiles.Count} config(s)…";

        try
        {
            await Task.Run(async () =>
            {
                using var gate = new SemaphoreSlim(3);
                await Task.WhenAll(profiles.Select(p => PingProfileAsync(p, gate)));
            });
            StatusText.Text = "ping complete";
        }
        finally
        {
            SetPinging(false);
        }
    }

    private async void OnPingOne(object sender, object e)
    {
        if (sender is not FrameworkElement { DataContext: V2RayProfile profile }) return;
        await Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(1);
            await PingProfileAsync(profile, gate);
        });
        StatusText.Text = $"{profile.Name}: {profile.Ping}";
    }

    private void OnExportConfig(object sender, object e)
    {
        if (sender is not FrameworkElement { DataContext: V2RayProfile profile }) return;
        var text = string.IsNullOrWhiteSpace(profile.RawUri) ? profile.Address : profile.RawUri;
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "nothing to export";
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        StatusText.Text = $"copied to clipboard: {profile.Name}";
    }

    private static V2RayProfile Clone(V2RayProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Protocol = p.Protocol,
        Mode = p.Mode,
        Address = p.Address,
        Port = p.Port,
        UserId = p.UserId,
        Security = p.Security,
        Transport = p.Transport,
        ServerName = p.ServerName,
        RawUri = p.RawUri,
    };
}
