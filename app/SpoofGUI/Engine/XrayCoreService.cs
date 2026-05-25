using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SpoofGUI.Core;
using SpoofGUI.Database;
using SpoofGUI.Models;

namespace SpoofGUI.Engine;

public sealed class XrayCoreService : IDisposable
{
    private const int SocksPort = 20882;
    private const int HttpPort = 20883;

    private readonly ILogger<XrayCoreService> _log;
    private readonly ProfileRepository _spoofProfiles;
    private Process? _proc;
    private bool _isRunning;

    public XrayCoreService(ILogger<XrayCoreService> log, ProfileRepository spoofProfiles)
    {
        _log = log;
        _spoofProfiles = spoofProfiles;
    }

    public bool IsRunning => _proc is { HasExited: false } || _isRunning;

    public Task<bool> RefreshRunningAsync()
    {
        if (_proc is { HasExited: true })
        {
            _proc.Dispose();
            _proc = null;
            _isRunning = false;
        }

        return Task.FromResult(IsRunning);
    }

    public async Task<string> VersionAsync()
    {
        var outText = await RunCaptureAsync(Paths.XrayExePath, ["version"]);
        return outText.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "xray ready";
    }

    public async Task StartAsync(V2RayProfile profile)
    {
        if (IsRunning) return;
        if (!File.Exists(Paths.XrayExePath))
            throw new FileNotFoundException($"xray not found: {Paths.XrayExePath}");

        ProxyPortKiller.KillOwners(SocksPort, HttpPort);

        var config = BuildProxyConfig(profile).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.XrayConfigPath)!);
        await File.WriteAllTextAsync(Paths.XrayConfigPath, config);
        AppLog.Info($"xray config written: socks 127.0.0.1:{SocksPort}, http 127.0.0.1:{HttpPort}");

        await RunCaptureAsync(Paths.XrayExePath, ["run", "-test", "-c", Paths.XrayConfigPath]);

        var psi = new ProcessStartInfo
        {
            FileName = Paths.XrayExePath,
            Arguments = $"run -c \"{Paths.XrayConfigPath}\"",
            WorkingDirectory = Path.GetDirectoryName(Paths.XrayExePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start xray");
        _proc.EnableRaisingEvents = true;
        _proc.Exited += (_, _) =>
        {
            _isRunning = false;
            AppLog.Warn($"xray exited code={_proc?.ExitCode}");
        };
        _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Warn($"xray: {e.Data}"); };
        _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Info($"xray: {e.Data}"); };
        _proc.BeginErrorReadLine();
        _proc.BeginOutputReadLine();

        _isRunning = true;
        AppLog.Info($"xray started pid={_proc.Id}, socks=127.0.0.1:{SocksPort}, http=127.0.0.1:{HttpPort}");
    }

    public async Task StopAsync()
    {
        if (_proc is not null)
        {
            try
            {
                if (!_proc.HasExited)
                {
                    _proc.Kill(entireProcessTree: true);
                    await _proc.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e) { _log.LogWarning(e, "xray stop failed"); }
            _proc.Dispose();
            _proc = null;
        }

        _isRunning = false;
        AppLog.Info("xray stopped");
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private static async Task<string> RunCaptureAsync(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {exe}");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim());
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    private JsonObject BuildProxyConfig(V2RayProfile profile)
    {
        return new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "socks-in",
                    ["listen"] = "127.0.0.1",
                    ["port"] = SocksPort,
                    ["protocol"] = "socks",
                    ["settings"] = new JsonObject { ["udp"] = true, ["auth"] = "noauth" },
                },
                new JsonObject
                {
                    ["tag"] = "http-in",
                    ["listen"] = "127.0.0.1",
                    ["port"] = HttpPort,
                    ["protocol"] = "http",
                },
            },
            ["outbounds"] = new JsonArray
            {
                BuildOutbound(profile),
                new JsonObject { ["protocol"] = "freedom", ["tag"] = "direct" },
                new JsonObject { ["protocol"] = "blackhole", ["tag"] = "block" },
            },
        };
    }

    private JsonObject BuildOutbound(V2RayProfile profile)
    {
        return profile.Protocol.ToLowerInvariant() switch
        {
            "vless" => BuildVlessOutbound(profile),
            "vmess" => ServerProtocol("vmess", profile, new JsonObject
            {
                ["id"] = profile.UserId,
                ["alterId"] = 0,
                ["security"] = "auto",
            }),
            "trojan" => BuildTrojanOutbound(profile),
            "ss" => BuildShadowsocksOutbound(profile),
            _ => throw new InvalidOperationException($"protocol not wired yet: {profile.Protocol}"),
        };
    }

    private JsonObject BuildVlessOutbound(V2RayProfile profile)
    {
        var query = ParseQuery(profile.RawUri);
        var user = new JsonObject { ["id"] = profile.UserId, ["encryption"] = "none" };
        if (query.TryGetValue("flow", out var flow)) user["flow"] = flow;
        return ServerProtocol("vless", profile, user);
    }

    private JsonObject BuildTrojanOutbound(V2RayProfile profile)
    {
        var outbound = new JsonObject
        {
            ["protocol"] = "trojan",
            ["tag"] = "proxy",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = NormalizeServerAddress(profile.Address),
                        ["port"] = profile.Port,
                        ["password"] = profile.UserId,
                    },
                },
            },
        };
        AddStreamSettings(outbound, profile);
        return outbound;
    }

    private JsonObject BuildShadowsocksOutbound(V2RayProfile profile)
    {
        var parts = profile.UserId.Split(':', 2);
        var method = parts.Length == 2 ? parts[0] : "aes-128-gcm";
        var password = parts.Length == 2 ? parts[1] : profile.UserId;
        return new JsonObject
        {
            ["protocol"] = "shadowsocks",
            ["tag"] = "proxy",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = NormalizeServerAddress(profile.Address),
                        ["port"] = profile.Port,
                        ["method"] = method,
                        ["password"] = password,
                    },
                },
            },
        };
    }

    private JsonObject ServerProtocol(string protocol, V2RayProfile profile, JsonObject user)
    {
        var outbound = new JsonObject
        {
            ["protocol"] = protocol,
            ["tag"] = "proxy",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = NormalizeServerAddress(profile.Address),
                        ["port"] = profile.Port,
                        ["users"] = new JsonArray { user },
                    },
                },
            },
        };
        AddStreamSettings(outbound, profile);
        return outbound;
    }

    private void AddStreamSettings(JsonObject outbound, V2RayProfile profile)
    {
        var query = ParseQuery(profile.RawUri);
        var network = string.IsNullOrWhiteSpace(profile.Transport) ? "tcp" : profile.Transport;
        var security = profile.Security.ToLowerInvariant() switch
        {
            "tls" => "tls",
            "reality" => "reality",
            _ => "none",
        };
        var serverName = string.IsNullOrWhiteSpace(profile.ServerName) ? NormalizeServerAddress(profile.Address) : profile.ServerName;
        var stream = new JsonObject { ["network"] = network, ["security"] = security };

        if (security == "tls")
        {
            stream["tlsSettings"] = new JsonObject { ["serverName"] = serverName, ["allowInsecure"] = false };
        }
        else if (security == "reality")
        {
            stream["realitySettings"] = new JsonObject
            {
                ["serverName"] = serverName,
                ["fingerprint"] = query.GetValueOrDefault("fp", "chrome"),
                ["publicKey"] = query.GetValueOrDefault("pbk", ""),
                ["shortId"] = query.GetValueOrDefault("sid", ""),
                ["spiderX"] = query.GetValueOrDefault("spx", ""),
            };
        }

        if (network.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            stream["wsSettings"] = new JsonObject
            {
                ["path"] = query.GetValueOrDefault("path", "/"),
                ["headers"] = new JsonObject { ["Host"] = query.GetValueOrDefault("host", serverName) },
            };
        }
        else if (network.Equals("grpc", StringComparison.OrdinalIgnoreCase))
        {
            stream["grpcSettings"] = new JsonObject { ["serviceName"] = query.GetValueOrDefault("serviceName", "") };
        }

        outbound["streamSettings"] = stream;
    }

    private static Dictionary<string, string> ParseQuery(string rawUri)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queryStart = rawUri.IndexOf('?');
        if (queryStart < 0) return map;

        var query = rawUri[(queryStart + 1)..];
        var fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0) query = query[..fragmentStart];

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            var key = WebUtility.UrlDecode(pair[..idx]);
            var value = WebUtility.UrlDecode(pair[(idx + 1)..]);
            if (!string.IsNullOrWhiteSpace(key)) map[key] = value;
        }

        return map;
    }

    private string NormalizeServerAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || !IPAddress.IsLoopback(ip))
            return address;

        var spoof = _spoofProfiles.GetActive();
        var replacement = spoof is null ? null : GetDefaultInterfaceIPv4(spoof.ConnectIp, spoof.ConnectPort);
        if (string.IsNullOrWhiteSpace(replacement) || IPAddress.IsLoopback(IPAddress.Parse(replacement)))
        {
            replacement = FirstNonLoopbackIPv4();
        }

        if (!string.IsNullOrWhiteSpace(replacement))
        {
            AppLog.Info($"xray: rewrote loopback target {address} to {replacement} because local loopback TCP is unavailable");
            return replacement;
        }

        return address;
    }

    private static string? GetDefaultInterfaceIPv4(string remoteIp, int remotePort)
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            socket.Connect(IPAddress.Parse(remoteIp), remotePort);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstNonLoopbackIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(addr.Address))
                {
                    return addr.Address.ToString();
                }
            }
        }

        return null;
    }
}
