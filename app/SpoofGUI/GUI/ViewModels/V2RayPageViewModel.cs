using System.Net;
using System.Text;
using System.Text.Json;
using SpoofGUI.Database;
using SpoofGUI.Engine;
using SpoofGUI.Models;

namespace SpoofGUI.GUI.ViewModels;

public sealed class V2RayPageViewModel
{
    private readonly V2RayProfileRepository _profiles;
    private readonly XrayCoreService _xray;
    private readonly TunnelService _tunnel;

    public V2RayPageViewModel(V2RayProfileRepository profiles, XrayCoreService xray, TunnelService tunnel)
    {
        _profiles = profiles;
        _xray = xray;
        _tunnel = tunnel;
    }

    public TunnelService Tunnel => _tunnel;

    public IReadOnlyList<V2RayProfile> LoadProfiles() => _profiles.All();
    public bool IsRunning => _xray.IsRunning;
    public Task<bool> RefreshRunningAsync() => _xray.RefreshRunningAsync();
    public Task<string> CoreVersionAsync() => _xray.VersionAsync();

    public V2RayProfile Import(string text, string mode)
    {
        var profile = V2RayConfigParser.Parse(text);
        profile.Mode = mode;
        _profiles.Upsert(profile);
        return profile;
    }

    public void Save(V2RayProfile profile) => _profiles.Upsert(profile);
    public void Delete(V2RayProfile profile) => _profiles.Delete(profile.Id);
    public Task StartAsync(V2RayProfile profile) => _xray.StartAsync(profile);
    public Task StopAsync() => _xray.StopAsync();
}

internal static class V2RayConfigParser
{
    public static V2RayProfile Parse(string text)
    {
        var raw = text.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return new V2RayProfile { Name = "custom config", Protocol = "custom", RawUri = raw };
        }

        return uri.Scheme.ToLowerInvariant() switch
        {
            "vless" => ParseCommonUri(uri, raw, "vless"),
            "trojan" => ParseCommonUri(uri, raw, "trojan"),
            "vmess" => ParseVmess(raw),
            "ss" => ParseShadowsocks(uri, raw),
            _ => ParseCommonUri(uri, raw, uri.Scheme.ToLowerInvariant()),
        };
    }

    private static V2RayProfile ParseCommonUri(Uri uri, string raw, string protocol)
    {
        var query = ParseQuery(uri.Query);
        var name = string.IsNullOrWhiteSpace(uri.Fragment)
            ? $"{protocol} {uri.Host}"
            : WebUtility.UrlDecode(uri.Fragment.TrimStart('#'));

        return new V2RayProfile
        {
            Name = name,
            Protocol = protocol,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            UserId = WebUtility.UrlDecode(uri.UserInfo),
            Security = query.GetValueOrDefault("security") ?? query.GetValueOrDefault("type") ?? "",
            Transport = query.GetValueOrDefault("type") ?? "tcp",
            ServerName = query.GetValueOrDefault("sni") ?? query.GetValueOrDefault("host") ?? "",
            RawUri = raw,
        };
    }

    private static V2RayProfile ParseVmess(string raw)
    {
        var payload = raw["vmess://".Length..].Trim();
        var json = DecodeBase64Url(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new V2RayProfile
        {
            Name = GetString(root, "ps", "vmess config"),
            Protocol = "vmess",
            Address = GetString(root, "add", ""),
            Port = int.TryParse(GetString(root, "port", "443"), out var port) ? port : 443,
            UserId = GetString(root, "id", ""),
            Security = GetString(root, "tls", ""),
            Transport = GetString(root, "net", "tcp"),
            ServerName = GetString(root, "sni", GetString(root, "host", "")),
            RawUri = raw,
        };
    }

    private static V2RayProfile ParseShadowsocks(Uri uri, string raw)
    {
        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            var payload = raw["ss://".Length..].Split('#', 2)[0].Split('?', 2)[0];
            var decoded = DecodeBase64Url(payload);
            if (Uri.TryCreate($"ss://{decoded}", UriKind.Absolute, out var decodedUri))
            {
                return ParseShadowsocks(decodedUri, raw);
            }
        }

        var name = string.IsNullOrWhiteSpace(uri.Fragment)
            ? $"ss {uri.Host}"
            : WebUtility.UrlDecode(uri.Fragment.TrimStart('#'));

        return new V2RayProfile
        {
            Name = name,
            Protocol = "ss",
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            UserId = WebUtility.UrlDecode(uri.UserInfo),
            RawUri = raw,
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                result[WebUtility.UrlDecode(parts[0])] = WebUtility.UrlDecode(parts[1]);
            }
        }

        return result;
    }

    private static string DecodeBase64Url(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static string GetString(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) ? value.GetString() ?? fallback : fallback;
}
