namespace SpoofGUI.Core;

internal static class CrashLog
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");

    public static void Write(string source, Exception? ex)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}\n{ex}\n\n";
            File.AppendAllText(LogPath, line);
        }
        catch { /* best effort */ }
    }
}
