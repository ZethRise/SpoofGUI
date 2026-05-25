using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using SpoofGUI.Core;
using SpoofGUI.Database;
using SpoofGUI.Models;

namespace SpoofGUI.Engine;

/// Manages a TUN-mode tunnel via xjasonlyu/tun2socks + wintun.
/// On Start: resolve upstream proxy host to IPv4, pin a /32 route to it via the
/// original default gateway, then spawn tun2socks pointing at xray's local
/// SOCKS5 inbound. tun2socks creates a wintun adapter; we IP-config it and add
/// 0.0.0.0/0 via the wintun gateway at metric 1. On Stop: kill tun2socks
/// (wintun goes away with it) and remove the pinned route.
public sealed class TunnelService : IDisposable
{
    private const string WintunDeviceName = "SpoofGUI-Tunnel";
    private const string WintunAddress = "198.18.0.1";
    private const string WintunMask = "255.255.255.0";
    private const string WintunGateway = "198.18.0.1";
    private const string FallbackDns = "8.8.8.8";
    private const string SocksHost = "127.0.0.1";
    private const int SocksPort = 20882;

    private readonly ILogger<TunnelService> _log;
    private readonly ProfileRepository _spoofProfiles;
    private Process? _proc;
    private string? _pinnedHostIp;
    private string? _originalGateway;

    public bool IsRunning => _proc is { HasExited: false };

    public TunnelService(ILogger<TunnelService> log, ProfileRepository spoofProfiles)
    {
        _log = log;
        _spoofProfiles = spoofProfiles;
    }

    public void Start(string upstreamHost)
    {
        if (IsRunning) return;
        if (!File.Exists(Paths.Tun2SocksExePath))
            throw new FileNotFoundException($"tun2socks not found: {Paths.Tun2SocksExePath}");
        if (!File.Exists(Paths.WintunDllPath))
            throw new FileNotFoundException($"wintun.dll not found: {Paths.WintunDllPath}");

        if (!WaitForLocalPort(SocksHost, SocksPort, TimeSpan.FromSeconds(8)))
            throw new InvalidOperationException(
                $"xray SOCKS5 inbound never came up on {SocksHost}:{SocksPort}. "
                + "Another app (e.g. Karing, v2rayN) may already hold this port.");

        var resolvedUpstream = ResolveIpv4(upstreamHost);
        var targetHost = resolvedUpstream ?? upstreamHost;

        if (!string.IsNullOrEmpty(resolvedUpstream) && IsLocalIp(resolvedUpstream))
        {
            var spoof = _spoofProfiles.GetActive();
            if (spoof is not null && !string.IsNullOrWhiteSpace(spoof.ConnectIp))
            {
                targetHost = spoof.ConnectIp;
                AppLog.Info($"tunnel: upstream resolved to local IP ({resolvedUpstream}), using active spoof target connect IP: {targetHost}");
            }
        }

        var hostIp = ResolveIpv4(targetHost);
        var (gatewayIp, _gatewayIface) = GetDefaultGateway()
            ?? throw new InvalidOperationException("could not detect default gateway");
        _originalGateway = gatewayIp;

        ClearBadLoopbackPin();

        if (!string.IsNullOrEmpty(hostIp) && ShouldPinRoute(hostIp))
        {
            RunNetsh($"route add {hostIp} mask 255.255.255.255 {gatewayIp} metric 1");
            _pinnedHostIp = hostIp;
            AppLog.Info($"tunnel: pinned {hostIp} via {gatewayIp}");
        }
        else if (!string.IsNullOrEmpty(hostIp))
        {
            AppLog.Info($"tunnel: skipped route pin for local upstream {hostIp}");
        }

        KillOrphanedTun2Socks();

        var psi = new ProcessStartInfo
        {
            FileName = Paths.Tun2SocksExePath,
            Arguments = $"-device tun://{WintunDeviceName} -proxy socks5://{SocksHost}:{SocksPort} -loglevel info",
            WorkingDirectory = Path.GetDirectoryName(Paths.Tun2SocksExePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        _proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start tun2socks");
        _proc.EnableRaisingEvents = true;
        _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Warn($"tun2socks: {e.Data}"); };
        _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Info($"tun2socks: {e.Data}"); };
        _proc.BeginErrorReadLine();
        _proc.BeginOutputReadLine();
        AppLog.Info($"tun2socks pid={_proc.Id}, waiting for wintun adapter...");

        var adapter = WaitForWintun(WintunDeviceName, TimeSpan.FromSeconds(15));
        if (adapter is null)
        {
            if (_proc.HasExited)
                throw new InvalidOperationException($"tun2socks exited with code {_proc.ExitCode} before creating wintun adapter");
            throw new InvalidOperationException("wintun adapter did not appear within 15s");
        }
        AppLog.Info($"tunnel: wintun adapter ready -> {adapter}");

        RunNetsh($"netsh interface ipv4 set address name=\"{adapter}\" source=static addr={WintunAddress} mask={WintunMask}", shell: true);
        RunNetsh($"netsh interface ipv4 set dnsservers name=\"{adapter}\" static address={FallbackDns} register=none validate=no", shell: true);
        RunNetsh($"netsh interface ipv4 add route 0.0.0.0/0 \"{adapter}\" {WintunGateway} metric=1", shell: true);

        AppLog.Info("tunnel: routes installed");
    }

    public void Stop()
    {
        // Order matters: delete the default route through wintun FIRST, while
        // the adapter is still present. If tun2socks dies first, the route
        // points at a phantom adapter and Windows keeps it long enough to
        // blackhole every other proxy app on the box (Karing, v2rayN, etc.).
        try { RunNetsh($"netsh interface ipv4 delete route 0.0.0.0/0 \"{WintunDeviceName}\"", shell: true); } catch { }
        ClearBadLoopbackPin();

        if (_pinnedHostIp is not null)
        {
            try { RunNetsh($"route delete {_pinnedHostIp}"); } catch { }
            _pinnedHostIp = null;
        }
        if (_proc is not null)
        {
            try
            {
                if (!_proc.HasExited)
                {
                    _proc.Kill(entireProcessTree: true);
                    _proc.WaitForExit(3000);
                }
            }
            catch (Exception e) { _log.LogWarning(e, "tun2socks kill"); }
            _proc.Dispose();
            _proc = null;
        }
        AppLog.Info("tunnel: stopped");
    }

    public void Dispose() => Stop();

    private static string? ResolveIpv4(string host)
    {
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return ip.ToString();
        try
        {
            var addrs = Dns.GetHostAddresses(host);
            foreach (var a in addrs)
                if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return a.ToString();
        }
        catch (Exception e) { AppLog.Warn($"dns resolve failed for {host}: {e.Message}"); }
        return null;
    }

    private static bool ShouldPinRoute(string hostIp)
    {
        if (string.IsNullOrEmpty(hostIp)) return false;
        if (!IPAddress.TryParse(hostIp, out var ip)) return false;
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast)) return false;
        if (IsLocalIp(hostIp)) return false;
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    private static bool IsLocalIp(string ipStr)
    {
        if (string.IsNullOrWhiteSpace(ipStr)) return false;
        if (!IPAddress.TryParse(ipStr, out var ip)) return false;
        if (IPAddress.IsLoopback(ip)) return true;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.Equals(ip)) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static void ClearBadLoopbackPin()
    {
        try { RunNetsh("route delete 127.0.0.1 mask 255.255.255.255"); } catch { }
    }

    private static (string gateway, string ifaceName)? GetDefaultGateway()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.Name.Contains(WintunDeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                var props = nic.GetIPProperties();
                foreach (var gw in props.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !gw.Address.Equals(IPAddress.Any))
                        return (gw.Address.ToString(), nic.Name);
                }
            }
        }
        catch { }
        return null;
    }

    private static bool WaitForLocalPort(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var sock = new System.Net.Sockets.TcpClient();
                var task = sock.ConnectAsync(host, port);
                if (task.Wait(500) && sock.Connected) return true;
            }
            catch { }
            Thread.Sleep(200);
        }
        return false;
    }

    private static string? WaitForWintun(string targetName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (nic.OperationalStatus == OperationalStatus.Up)
                    {
                        return nic.Name;
                    }
                }
            }
            Thread.Sleep(200);
        }
        return null;
    }

    private static void KillOrphanedTun2Socks()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("tun2socks"))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(2000);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void RunNetsh(string command, bool shell = false)
    {
        ProcessStartInfo psi;
        if (shell)
        {
            psi = new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
        }
        else
        {
            var parts = command.Split(' ', 2);
            psi = new ProcessStartInfo(parts[0], parts.Length > 1 ? parts[1] : "")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
        }
        using var p = Process.Start(psi);
        if (p is null) return;
        p.WaitForExit(5000);
        if (p.ExitCode != 0)
        {
            var err = p.StandardError.ReadToEnd().Trim();
            if (!string.IsNullOrEmpty(err)) AppLog.Warn($"netsh: {err}");
        }
    }
}
