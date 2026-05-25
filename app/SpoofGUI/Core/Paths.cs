namespace SpoofGUI.Core;

internal static class Paths
{
    public static string AppDataDir
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(root, "SpoofGUI");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(AppDataDir, "spoofgui.db");

    public static string XrayDir => Path.Combine(AppContext.BaseDirectory, "Xray");
    public static string XrayExePath => Path.Combine(XrayDir, "xray.exe");
    public static string XrayConfigPath => Path.Combine(AppDataDir, "xray-client.json");

    public static string PatternEngineExePath =>
        Path.Combine(AppContext.BaseDirectory, "engine", "SpoofGUI.SniSpoofEngine.exe");

    public static string PatternEngineConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "engine", "config.json");

    public static string Tun2SocksExePath =>
        Path.Combine(AppContext.BaseDirectory, "engine", "tun2socks.exe");

    public static string WintunDllPath =>
        Path.Combine(AppContext.BaseDirectory, "engine", "wintun.dll");
}
