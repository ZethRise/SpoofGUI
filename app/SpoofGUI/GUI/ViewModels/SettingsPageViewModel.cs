using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SpoofGUI.Database;

namespace SpoofGUI.GUI.ViewModels;

public sealed class SettingsPageViewModel
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/ZethRise/SpoofGUI/releases?per_page=10";
    public const string ReleasesPageUrl = "https://github.com/ZethRise/SpoofGUI/releases";

    private readonly SettingsRepository _settings;
    private readonly ILogger<SettingsPageViewModel> _log;

    public SettingsPageViewModel(SettingsRepository settings, ILogger<SettingsPageViewModel> log)
    {
        _settings = settings;
        _log = log;
    }

    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    public string Theme
    {
        get => _settings.Get("theme") ?? "dark";
    }

    public void SetTheme(string t) => _settings.Set("theme", t);

    public string LastUpdateCheckText()
    {
        var ts = _settings.Get("last_update_check");
        return string.IsNullOrEmpty(ts) ? "never checked" : $"last check: {ts}";
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        var url = _settings.Get("update_repo_url") ?? ReleasesApiUrl;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpoofGUI");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _settings.Set("last_update_check", now);

            var release = GetLatestPublishedRelease(doc.RootElement);
            if (release is null)
            {
                return new UpdateCheckResult("no SpoofGUI releases published yet", $"last check: {now}", ReleasesPageUrl, false);
            }

            var tag = release.Value.GetProperty("tag_name").GetString() ?? "?";
            var releaseUrl = release.Value.TryGetProperty("html_url", out var html)
                ? html.GetString() ?? ReleasesPageUrl
                : ReleasesPageUrl;

            var installed = GetInstalledVersion();
            var latest = TryParseVersion(tag);
            if (latest is null)
            {
                return new UpdateCheckResult($"latest SpoofGUI: {tag}", $"last check: {now}", releaseUrl, false);
            }

            var isNewer = latest > installed;
            var status = isNewer
                ? $"update available: {tag} (installed: {installed.ToString(3)})"
                : $"up to date: {installed.ToString(3)} (latest: {tag})";

            return new UpdateCheckResult(status, $"last check: {now}", releaseUrl, isNewer);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            _log.LogWarning(e, "update channel not found");
            return new UpdateCheckResult(
                "release channel unavailable: repo is private, missing, or not published",
                LastUpdateCheckText(),
                ReleasesPageUrl,
                false);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "update check failed");
            return new UpdateCheckResult($"check failed: {e.Message}", LastUpdateCheckText(), ReleasesPageUrl, false);
        }
    }

    private Version GetInstalledVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

    private static Version? TryParseVersion(string tag)
    {
        var match = Regex.Match(tag, @"\d+(?:\.\d+){0,3}");
        return match.Success && Version.TryParse(match.Value, out var version) ? version : null;
    }

    private static JsonElement? GetLatestPublishedRelease(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            return root;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var release in root.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            return release;
        }

        return null;
    }
}

public sealed record UpdateCheckResult(
    string StatusText,
    string LastCheckText,
    string ReleaseUrl,
    bool IsUpdateAvailable);
