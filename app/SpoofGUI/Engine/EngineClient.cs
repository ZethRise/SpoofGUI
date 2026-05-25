using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoofGUI.Core;
using SpoofGUI.Models;

namespace SpoofGUI.Engine;

public sealed class EngineClient : IAsyncDisposable
{
    private readonly EngineSupervisor _supervisor;
    private readonly ILogger<EngineClient> _log;
    private CancellationTokenSource? _statsCts;
    private int _listenPort;

    public event Action<string, JsonElement>? EventReceived;

    public EngineClient(EngineSupervisor supervisor, ILogger<EngineClient> log)
    {
        _supervisor = supervisor;
        _log = log;
    }

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<JsonElement> PingAsync(CancellationToken ct = default) =>
        Task.FromResult(JsonSerializer.SerializeToElement(new { pong = true }));

    public async Task<string> StartSpoofAsync(SpoofProfile p, CancellationToken ct = default)
    {
        try
        {
            _supervisor.Start(p);
            if (!_supervisor.IsRunning)
                throw new InvalidOperationException(_supervisor.GetStartupFailureMessage());

            if (!_supervisor.WaitForListener(p.ListenHost, p.ListenPort, TimeSpan.FromSeconds(6)))
                throw new InvalidOperationException(_supervisor.GetStartupFailureMessage());

            AppLog.Info("engine accepted start command");
            _listenPort = p.ListenPort;
            StartStatsTicker();
            return GetDefaultInterfaceIPv4(p.ConnectIp, p.ConnectPort);
        }
        catch (Exception e)
        {
            _log.LogError(e, "engine start failed");
            AppLog.Error($"engine start failed: {e.Message}");
            throw;
        }
    }

    public Task StopSpoofAsync(CancellationToken ct = default)
    {
        _statsCts?.Cancel();
        _statsCts?.Dispose();
        _statsCts = null;
        _supervisor.Stop();
        return Task.CompletedTask;
    }

    public Task<EngineStatus> StatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new EngineStatus
        {
            Running = _supervisor.IsRunning,
            UptimeMs = (ulong)_supervisor.Uptime.TotalMilliseconds,
            Connections = _supervisor.IsRunning && _listenPort > 0
                ? NetStats.CountEstablishedOnLocalPort(_listenPort)
                : 0,
        });

    private void StartStatsTicker()
    {
        _statsCts?.Cancel();
        _statsCts = new CancellationTokenSource();
        var token = _statsCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                if (!_supervisor.IsRunning) continue;
                var conns = _listenPort > 0 ? NetStats.CountEstablishedOnLocalPort(_listenPort) : 0;
                var payload = JsonSerializer.SerializeToElement(new
                {
                    running = true,
                    uptime_ms = (ulong)_supervisor.Uptime.TotalMilliseconds,
                    connections = conns,
                });
                EventReceived?.Invoke("stats", payload);
            }
        }, token);
    }

    public void EnsureStatsTicker()
    {
        if (_supervisor.IsRunning && (_statsCts is null || _statsCts.IsCancellationRequested))
            StartStatsTicker();
    }

    private static string GetDefaultInterfaceIPv4(string remoteIp, int remotePort)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(IPAddress.Parse(remoteIp), remotePort);
            var ip = (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "";
            if (string.IsNullOrEmpty(ip) || NetworkHelper.IsVirtualInterface(ip))
            {
                return NetworkHelper.GetLocalPhysicalIPAddress();
            }
            return ip;
        }
        catch
        {
            return NetworkHelper.GetLocalPhysicalIPAddress();
        }
    }

    public ValueTask DisposeAsync()
    {
        _statsCts?.Cancel();
        _statsCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
