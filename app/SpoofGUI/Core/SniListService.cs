using System.Net.Http;
using System.Text.Json;

namespace SpoofGUI.Core;

public sealed record SniListEntry(string Sni, string Ip, int Port);

public sealed class SniListService
{
    public const string SniListUrl = "https://raw.githubusercontent.com/ZethRise/SpoofGUI/master/sni.json";

    public async Task<IReadOnlyList<SniListEntry>> FetchAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SpoofGUI");

        var json = await http.GetStringAsync(SniListUrl, ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("sni.json is not a JSON array");

        var entries = new List<SniListEntry>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var sni = GetString(item, "sni");
            var ip = GetString(item, "ip");
            if (string.IsNullOrWhiteSpace(sni) || string.IsNullOrWhiteSpace(ip)) continue;
            var port = item.TryGetProperty("port", out var p) && p.TryGetInt32(out var parsed) ? parsed : 443;
            entries.Add(new SniListEntry(sni.Trim(), ip.Trim(), port));
        }

        return entries;
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
