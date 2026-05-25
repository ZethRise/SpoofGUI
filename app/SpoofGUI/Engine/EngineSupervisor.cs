using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoofGUI.Core;
using SpoofGUI.Models;

namespace SpoofGUI.Engine;

public sealed class EngineSupervisor : IDisposable
{
    private readonly ILogger<EngineSupervisor> _log;
    private Process? _proc;
    private DateTimeOffset? _startedAt;

    public bool IsRunning => _proc is { HasExited: false };
    public TimeSpan Uptime => _startedAt is null ? TimeSpan.Zero : DateTimeOffset.Now - _startedAt.Value;

    public EngineSupervisor(ILogger<EngineSupervisor> log) => _log = log;

    public void Start(SpoofProfile profile)
    {
        if (IsRunning) return;

        var exe = Paths.PatternEngineExePath;
        if (!File.Exists(exe))
            throw new FileNotFoundException($"engine binary not found: {exe}");

        ProxyPortKiller.KillOwners(profile.ListenPort);
        WriteConfig(profile);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start engine");
        _startedAt = DateTimeOffset.Now;
        _proc.EnableRaisingEvents = true;
        _proc.Exited += (_, _) =>
        {
            _log.LogWarning("engine exited code={code}", _proc?.ExitCode);
            AppLog.Warn($"engine exited code={_proc?.ExitCode}");
        };

        AppLog.Info($"engine process started pid={_proc.Id}");
    }

    public bool WaitForListener(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning) return false;
            if (IsListening(host, port)) return true;
            Thread.Sleep(100);
        }

        return false;
    }

    public string GetStartupFailureMessage()
    {
        if (_proc is { HasExited: true })
        {
            return $"engine exited after start (code {_proc.ExitCode})";
        }

        return "engine did not stay running. Run SpoofGUI as administrator and check Logs.";
    }

    public void Stop()
    {
        if (_proc is null) return;
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); }
        catch (Exception e) { _log.LogWarning(e, "stop"); }
        AppLog.Info("engine process stopped");
        _proc.Dispose();
        _proc = null;
        _startedAt = null;
    }

    private static void WriteConfig(SpoofProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.PatternEngineConfigPath)!);
        var config = new
        {
            LISTEN_HOST = profile.ListenHost,
            LISTEN_PORT = profile.ListenPort,
            CONNECT_IP = profile.ConnectIp,
            CONNECT_PORT = profile.ConnectPort,
            FAKE_SNI = profile.FakeSni,
        };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Paths.PatternEngineConfigPath, json);
        AppLog.Info($"config written: {profile.ListenHost}:{profile.ListenPort} -> {profile.ConnectIp}:{profile.ConnectPort}; fake_sni {profile.FakeSni}");
    }

    private static bool IsListening(string host, int port)
    {
        var expected = IPAddress.TryParse(host, out var parsed) ? parsed : IPAddress.Any;
        foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
        {
            if (ep.Port != port) continue;
            if (expected.Equals(IPAddress.Any) || ep.Address.Equals(IPAddress.Any) || ep.Address.Equals(expected))
                return true;
        }

        return false;
    }

    public void Dispose() => Stop();
}
