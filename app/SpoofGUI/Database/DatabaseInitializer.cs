using Microsoft.Data.Sqlite;

namespace SpoofGUI.Database;

public sealed class DatabaseInitializer
{
    private readonly DatabaseConnection _db;
    public DatabaseInitializer(DatabaseConnection db) => _db = db;

    public void EnsureCreated()
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Exec(conn, tx, Schema.Profiles);
        Exec(conn, tx, Schema.V2RayProfiles);
        Exec(conn, tx, Schema.Settings);
        Exec(conn, tx, Schema.SeedSettings);
        Exec(conn, tx, Schema.MigrateUpdateRepoUrl);
        Exec(conn, tx, Schema.SeedDefaultProfile);
        tx.Commit();
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

internal static class Schema
{
    public const string Profiles = @"
CREATE TABLE IF NOT EXISTS profiles (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    name         TEXT    NOT NULL UNIQUE,
    listen_host  TEXT    NOT NULL DEFAULT '0.0.0.0',
    listen_port  INTEGER NOT NULL DEFAULT 40443,
    connect_ip   TEXT    NOT NULL,
    connect_port INTEGER NOT NULL DEFAULT 443,
    fake_sni     TEXT    NOT NULL,
    is_active    INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT    NOT NULL DEFAULT (datetime('now'))
);";

    public const string V2RayProfiles = @"
CREATE TABLE IF NOT EXISTS v2ray_profiles (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    protocol    TEXT    NOT NULL,
    mode        TEXT    NOT NULL DEFAULT 'Proxy',
    address     TEXT    NOT NULL DEFAULT '',
    port        INTEGER NOT NULL DEFAULT 443,
    user_id     TEXT    NOT NULL DEFAULT '',
    security    TEXT    NOT NULL DEFAULT '',
    transport   TEXT    NOT NULL DEFAULT 'tcp',
    server_name TEXT    NOT NULL DEFAULT '',
    raw_uri     TEXT    NOT NULL DEFAULT '',
    created_at  TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now'))
);";

    public const string Settings = @"
CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);";

    public const string SeedSettings = @"
INSERT OR IGNORE INTO settings (key, value) VALUES
    ('theme',           'dark'),
    ('update_repo_url', 'https://api.github.com/repos/ZethRise/SpoofGUI/releases?per_page=10'),
    ('last_update_check', '');";

    public const string MigrateUpdateRepoUrl = @"
UPDATE settings
SET value = 'https://api.github.com/repos/ZethRise/SpoofGUI/releases?per_page=10'
WHERE key = 'update_repo_url'
  AND value IN (
      'https://api.github.com/repos/patterniha/SNI-Spoofing/releases/latest',
      'https://api.github.com/repos/ZethRise/SpoofGUI/releases/latest',
      'https://github.com/ZethRise/SpoofGUI',
      'https://github.com/ZethRise/SpoofGUI/releases'
  );";

    public const string SeedDefaultProfile = @"
INSERT OR IGNORE INTO profiles (name, listen_host, listen_port, connect_ip, connect_port, fake_sni, is_active)
VALUES ('default', '0.0.0.0', 40443, '104.19.229.21', 443, 'www.hcaptcha.com', 1);";
}
