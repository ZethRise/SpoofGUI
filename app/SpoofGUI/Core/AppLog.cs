using System.Collections.Concurrent;

namespace SpoofGUI.Core;

public static class AppLog
{
    private const int MaxEntries = 500;
    private static readonly ConcurrentQueue<string> Entries = new();

    public static void Info(string message) => Add("info", message);
    public static void Warn(string message) => Add("warn", message);
    public static void Error(string message) => Add("error", message);

    public static IReadOnlyList<string> Snapshot() => Entries.ToArray();

    public static void Clear()
    {
        while (Entries.TryDequeue(out _)) { }
    }

    private static void Add(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        Entries.Enqueue(line);
        while (Entries.Count > MaxEntries && Entries.TryDequeue(out _)) { }
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpoofGUI");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "app.log"), line + Environment.NewLine);
        }
        catch { }
    }
}
