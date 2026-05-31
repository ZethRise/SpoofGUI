using SpoofGUI.Database;

namespace SpoofGUI.Core;

public sealed class AppSettings
{
    private const string AllowInsecureKey = "xray_allow_insecure";
    private const string LogLevelKey = "xray_log_level";
    private const string V2RayModeKey = "default_import_mode";
    private const string CheckUpdatesOnLaunchKey = "check_updates_on_launch";
    private const string FastModeKey = "fast_mode";
    private const string RemoteDnsKey = "remote_dns";
    private const string DirectDnsKey = "direct_dns";
    private const string BootstrapDnsKey = "bootstrap_dns";
    private const string DnsStrategyKey = "dns_strategy";

    private static readonly string[] ValidLogLevels = ["none", "error", "warning", "info", "debug"];
    private static readonly string[] ValidModes = ["Proxy", "Tunnel", "SystemProxy"];
    private static readonly string[] ValidDnsStrategies = ["prefer_ipv4", "prefer_ipv6", "ipv4_only", "ipv6_only"];

    public const string DefaultRemoteDns = "8.8.8.8";
    public const string DefaultDirectDns = "1.1.1.1";
    public const string DefaultBootstrapDns = "223.5.5.5";
    public const string DefaultDnsStrategy = "prefer_ipv4";

    private readonly SettingsRepository _settings;
    public AppSettings(SettingsRepository settings) => _settings = settings;

    public bool XrayAllowInsecure
    {
        get => ReadBool(AllowInsecureKey, false);
        set => _settings.Set(AllowInsecureKey, value ? "1" : "0");
    }

    public string XrayLogLevel
    {
        get
        {
            var value = _settings.Get(LogLevelKey);
            return value is not null && ValidLogLevels.Contains(value) ? value : "warning";
        }
        set => _settings.Set(LogLevelKey, ValidLogLevels.Contains(value) ? value : "warning");
    }

    public string V2RayMode
    {
        get
        {
            var value = _settings.Get(V2RayModeKey);
            return value is not null && ValidModes.Contains(value) ? value : "Proxy";
        }
        set => _settings.Set(V2RayModeKey, ValidModes.Contains(value) ? value : "Proxy");
    }

    public bool CheckUpdatesOnLaunch
    {
        get => ReadBool(CheckUpdatesOnLaunchKey, false);
        set => _settings.Set(CheckUpdatesOnLaunchKey, value ? "1" : "0");
    }

    public bool FastMode
    {
        get => ReadBool(FastModeKey, false);
        set => _settings.Set(FastModeKey, value ? "1" : "0");
    }

    public string RemoteDns
    {
        get => ReadString(RemoteDnsKey, DefaultRemoteDns);
        set => _settings.Set(RemoteDnsKey, Clean(value, DefaultRemoteDns));
    }

    public string DirectDns
    {
        get => ReadString(DirectDnsKey, DefaultDirectDns);
        set => _settings.Set(DirectDnsKey, Clean(value, DefaultDirectDns));
    }

    public string BootstrapDns
    {
        get => ReadString(BootstrapDnsKey, DefaultBootstrapDns);
        set => _settings.Set(BootstrapDnsKey, Clean(value, DefaultBootstrapDns));
    }

    public string DnsStrategy
    {
        get
        {
            var value = _settings.Get(DnsStrategyKey);
            return value is not null && ValidDnsStrategies.Contains(value) ? value : DefaultDnsStrategy;
        }
        set => _settings.Set(DnsStrategyKey, ValidDnsStrategies.Contains(value) ? value : DefaultDnsStrategy);
    }

    private string ReadString(string key, string fallback)
    {
        var value = _settings.Get(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string Clean(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private bool ReadBool(string key, bool fallback)
    {
        var value = _settings.Get(key);
        if (value is null) return fallback;
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
