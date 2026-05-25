using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using SpoofGUI.Database;
using SpoofGUI.Engine;
using SpoofGUI.GUI.Pages;
using SpoofGUI.Core;
using SpoofGUI.Models;

namespace SpoofGUI.GUI.ViewModels;

public sealed class MainPageViewModel
{
    private readonly ProfileRepository _profiles;
    private readonly EngineClient _engine;
    private readonly ILogger<MainPageViewModel> _log;

    private MainPage? _page;
    private DispatcherQueue? _dispatcher;
    private string _iface = "—";
    private Action<string, JsonElement>? _handler;

    public MainPageViewModel(ProfileRepository profiles, EngineClient engine, ILogger<MainPageViewModel> log)
    {
        _profiles = profiles;
        _engine = engine;
        _log = log;
    }

    public async Task LoadAsync(MainPage page)
    {
        _page = page;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        if (_handler is null)
        {
            _handler = OnEngineEvent;
            _engine.EventReceived += _handler;
        }
        var p = _profiles.GetActive();
        if (p is null) { page.RenderError("no active profile"); return; }
        var flow = $"{DisplayListenHost(p)}:{p.ListenPort}  ->  {p.ConnectIp}:{p.ConnectPort}";

        EngineStatus? status = null;
        try
        {
            status = await _engine.StatusAsync();
            AppLog.Info($"MainPage Loaded: engine status.Running={status.Running}, uptime={status.UptimeMs}ms, conns={status.Connections}");
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "engine status query failed on load");
            AppLog.Warn($"engine status failed: {e.Message}");
        }

        if (status is { Running: true })
        {
            if (string.IsNullOrEmpty(_iface) || _iface == "—")
                _iface = "active";
            _engine.EnsureStatsTicker();
            page.RenderLive(_iface, status.UptimeMs, status.Connections);
            return;
        }

        page.RenderIdle(p.Name, flow, p.FakeSni);
    }

    public async Task ConnectAsync()
    {
        if (_page is null) return;
        var p = _profiles.GetActive();
        if (p is null) { _page.RenderError("no active profile"); return; }

        _page.RenderConnecting();
        try
        {
            _iface = await _engine.StartSpoofAsync(p);
            AppLog.Info($"listener ready on {DisplayListenHost(p)}:{p.ListenPort}; target {p.ConnectIp}:{p.ConnectPort}; fake_sni {p.FakeSni}");
            _page.RenderLive(_iface, 0, 0);
        }
        catch (Exception e)
        {
            _log.LogError(e, "connect failed");
            AppLog.Error($"start failed: {e.Message}");
            _page.RenderError(e.Message);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_page is null) return;
        try
        {
            await _engine.StopSpoofAsync();
            AppLog.Info("listener stopped");
            _iface = "—";
            await LoadAsync(_page);
        }
        catch (Exception e)
        {
            _log.LogError(e, "disconnect failed");
            AppLog.Error($"stop failed: {e.Message}");
            _page.RenderError(e.Message);
        }
    }

    private void OnEngineEvent(string name, JsonElement data)
    {
        if (name != "stats") return;
        if (_page is null || _dispatcher is null) return;
        if (!data.GetProperty("running").GetBoolean()) return;

        var uptime = data.GetProperty("uptime_ms").GetUInt64();
        var conns = data.GetProperty("connections").GetUInt32();
        var iface = _iface;
        _dispatcher.TryEnqueue(() => _page?.RenderLive(iface, uptime, conns));
    }

    private static string DisplayListenHost(SpoofProfile profile)
    {
        if (profile.ListenHost.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }
        if (profile.ListenHost.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return NetworkHelper.GetLocalPhysicalIPAddress();
        }
        return profile.ListenHost;
    }
}
