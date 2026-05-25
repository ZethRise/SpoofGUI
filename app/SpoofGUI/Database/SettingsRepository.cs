using Microsoft.Data.Sqlite;

namespace SpoofGUI.Database;

public sealed class SettingsRepository
{
    private readonly DatabaseConnection _db;
    public SettingsRepository(DatabaseConnection db) => _db = db;

    public string? Get(string key)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO settings (key, value) VALUES ($k, $v)
ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }
}
