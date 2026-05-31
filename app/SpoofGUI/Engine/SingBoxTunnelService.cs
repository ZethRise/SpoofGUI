using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SpoofGUI.Core;
using SpoofGUI.Models;

namespace SpoofGUI.Engine;

public sealed class SingBoxTunnelService : IDisposable
{
    private const string TunDeviceName = "SpoofGUI-Tunnel";
    private const string TunAddress = "198.18.0.1/30";

    private readonly ILogger<SingBoxTunnelService> _log;
    private readonly AppSettings _appSettings;
    private Process? _proc;

    public bool IsRunning => _proc is { HasExited: false };

    public SingBoxTunnelService(ILogger<SingBoxTunnelService> log, AppSettings appSettings)
    {
        _log = log;
        _appSettings = appSettings;
    }

    public void Start(V2RayProfile profile)
    {
        if (IsRunning) return;
        if (!File.Exists(Paths.SingBoxExePath))
            throw new FileNotFoundException($"sing-box not found: {Paths.SingBoxExePath}");

        KillOrphaned();

        var config = BuildConfig(profile).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.SingBoxConfigPath)!);
        File.WriteAllText(Paths.SingBoxConfigPath, config);
        AppLog.Info("sing-box tun config written");

        var psi = new ProcessStartInfo
        {
            FileName = Paths.SingBoxExePath,
            Arguments = $"run -c \"{Paths.SingBoxConfigPath}\"",
            WorkingDirectory = Path.GetDirectoryName(Paths.SingBoxExePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        _proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start sing-box");
        _proc.EnableRaisingEvents = true;
        _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Warn($"sing-box: {e.Data}"); };
        _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Info($"sing-box: {e.Data}"); };
        _proc.BeginErrorReadLine();
        _proc.BeginOutputReadLine();
        AppLog.Info($"sing-box tun started pid={_proc.Id}");

        if (_proc.WaitForExit(1500))
            throw new InvalidOperationException($"sing-box exited immediately (code {_proc.ExitCode}); check Logs");
    }

    public void Stop()
    {
        if (_proc is not null)
        {
            try
            {
                if (!_proc.HasExited)
                {

                    TryGracefulStop(_proc.Id);
                    if (!_proc.WaitForExit(4000))
                    {
                        _proc.Kill(entireProcessTree: true);
                        _proc.WaitForExit(3000);
                    }
                }
            }
            catch (Exception e) { _log.LogWarning(e, "sing-box stop"); }
            _proc.Dispose();
            _proc = null;
        }

        try { RunShell("ipconfig /flushdns"); } catch { }
        AppLog.Info("sing-box tun stopped");
    }

    public void Dispose() => Stop();

    private static void TryGracefulStop(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo("taskkill", $"/T /PID {pid}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
        }
        catch { }
    }

    private static void KillOrphaned()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("sing-box"))
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(2000); } catch { }
            }
        }
        catch { }
    }

    private static void RunShell(string command)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }

    private JsonObject BuildConfig(V2RayProfile profile)
    {
        return new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn", ["timestamp"] = true },

            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    DnsServer("remote", _appSettings.RemoteDns, "proxy"),
                    DnsServer("local", _appSettings.DirectDns, null),
                    DnsServer("bootstrap", _appSettings.BootstrapDns, null),
                },
                ["final"] = "remote",
                ["strategy"] = _appSettings.DnsStrategy,
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = TunDeviceName,
                    ["address"] = new JsonArray { TunAddress },
                    ["mtu"] = 1500,
                    ["auto_route"] = true,

                    ["strict_route"] = false,
                    ["stack"] = "gvisor",
                },
            },
            ["outbounds"] = new JsonArray
            {
                BuildOutbound(profile),
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
            },
            ["route"] = new JsonObject
            {

                ["rules"] = new JsonArray
                {
                    new JsonObject { ["action"] = "sniff" },
                    new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                    new JsonObject
                    {
                        ["ip_cidr"] = new JsonArray
                        {
                            "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
                            "169.254.0.0/16", "100.64.0.0/10", "224.0.0.0/4",
                        },
                        ["outbound"] = "direct",
                    },
                },
                ["auto_detect_interface"] = true,

                ["default_domain_resolver"] = new JsonObject { ["server"] = "bootstrap" },
                ["final"] = "proxy",
            },
        };
    }

    private static JsonObject DnsServer(string tag, string value, string? detour)
    {
        value = (value ?? "").Trim();
        var node = new JsonObject { ["tag"] = tag };
        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex > 0)
        {
            var scheme = value[..schemeIndex].ToLowerInvariant();
            var rest = value[(schemeIndex + 3)..];
            var slash = rest.IndexOf('/');
            var host = slash >= 0 ? rest[..slash] : rest;
            var path = slash >= 0 ? rest[slash..] : null;
            node["type"] = scheme switch
            {
                "https" => "https",
                "tls" => "tls",
                "quic" => "quic",
                "h3" => "h3",
                _ => "udp",
            };
            node["server"] = host;
            if (path is not null && scheme is "https" or "h3") node["path"] = path;
        }
        else
        {
            node["type"] = "udp";
            node["server"] = value;
        }

        if (detour is not null) node["detour"] = detour;
        return node;
    }

    private JsonObject BuildOutbound(V2RayProfile profile)
    {
        var proto = profile.Protocol.ToLowerInvariant();
        var query = ParseQuery(profile.RawUri);
        var outbound = new JsonObject
        {
            ["type"] = proto,
            ["tag"] = "proxy",
            ["server"] = profile.Address,
            ["server_port"] = profile.Port,
        };

        switch (proto)
        {
            case "vless":
                outbound["uuid"] = profile.UserId;
                if (query.TryGetValue("flow", out var flow) && !string.IsNullOrWhiteSpace(flow))
                    outbound["flow"] = flow;
                break;
            case "vmess":
                outbound["uuid"] = profile.UserId;
                outbound["security"] = "auto";
                outbound["alter_id"] = 0;
                break;
            case "trojan":
                outbound["password"] = profile.UserId;
                break;
            case "shadowsocks":
            case "ss":
                outbound["type"] = "shadowsocks";
                var parts = profile.UserId.Split(':', 2);
                outbound["method"] = parts.Length == 2 ? parts[0] : "aes-128-gcm";
                outbound["password"] = parts.Length == 2 ? parts[1] : profile.UserId;
                break;
            default:
                throw new InvalidOperationException(
                    $"Tunnel mode (sing-box) does not support protocol '{profile.Protocol}'. Use Proxy or System Proxy mode.");
        }

        AddTransportAndTls(outbound, profile, query);
        return outbound;
    }

    private static void AddTransportAndTls(JsonObject outbound, V2RayProfile profile, Dictionary<string, string> query)
    {
        var network = string.IsNullOrWhiteSpace(profile.Transport) ? "tcp" : profile.Transport.ToLowerInvariant();
        var security = profile.Security.ToLowerInvariant();
        var serverName = string.IsNullOrWhiteSpace(profile.ServerName) ? profile.Address : profile.ServerName;

        if (security is "tls" or "reality")
        {
            var tls = new JsonObject
            {
                ["enabled"] = true,
                ["server_name"] = serverName,
                ["insecure"] = false,
                ["utls"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["fingerprint"] = query.GetValueOrDefault("fp", "chrome"),
                },
            };
            if (security == "reality")
            {
                tls["reality"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["public_key"] = query.GetValueOrDefault("pbk", ""),
                    ["short_id"] = query.GetValueOrDefault("sid", ""),
                };
            }
            outbound["tls"] = tls;
        }

        if (network == "ws")
        {
            outbound["transport"] = new JsonObject
            {
                ["type"] = "ws",
                ["path"] = query.GetValueOrDefault("path", "/"),
                ["headers"] = new JsonObject { ["Host"] = query.GetValueOrDefault("host", serverName) },
            };
        }
        else if (network == "grpc")
        {
            outbound["transport"] = new JsonObject
            {
                ["type"] = "grpc",
                ["service_name"] = query.GetValueOrDefault("serviceName", ""),
            };
        }
    }

    private static Dictionary<string, string> ParseQuery(string rawUri)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var q = rawUri.IndexOf('?');
        if (q < 0) return map;
        var query = rawUri[(q + 1)..];
        var frag = query.IndexOf('#');
        if (frag >= 0) query = query[..frag];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            var key = WebUtility.UrlDecode(pair[..idx]);
            var val = WebUtility.UrlDecode(pair[(idx + 1)..]);
            if (!string.IsNullOrWhiteSpace(key)) map[key] = val;
        }
        return map;
    }
}
