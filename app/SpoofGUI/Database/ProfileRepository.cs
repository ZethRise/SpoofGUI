using Microsoft.Data.Sqlite;
using SpoofGUI.Models;

namespace SpoofGUI.Database;

public sealed class ProfileRepository
{
    private readonly DatabaseConnection _db;
    public ProfileRepository(DatabaseConnection db) => _db = db;

    public IReadOnlyList<SpoofProfile> All()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, listen_host, listen_port, connect_ip, connect_port, fake_sni, is_active
FROM profiles ORDER BY id;";
        var list = new List<SpoofProfile>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public SpoofProfile? GetActive()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, listen_host, listen_port, connect_ip, connect_port, fake_sni, is_active
FROM profiles WHERE is_active = 1 LIMIT 1;";
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    public void Upsert(SpoofProfile p)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO profiles (id, name, listen_host, listen_port, connect_ip, connect_port, fake_sni, is_active, updated_at)
VALUES ($id, $name, $lh, $lp, $ci, $cp, $sni, $act, datetime('now'))
ON CONFLICT(id) DO UPDATE SET
    name=excluded.name,
    listen_host=excluded.listen_host,
    listen_port=excluded.listen_port,
    connect_ip=excluded.connect_ip,
    connect_port=excluded.connect_port,
    fake_sni=excluded.fake_sni,
    is_active=excluded.is_active,
    updated_at=datetime('now');";
        cmd.Parameters.AddWithValue("$id", p.Id == 0 ? DBNull.Value : p.Id);
        cmd.Parameters.AddWithValue("$name", p.Name);
        cmd.Parameters.AddWithValue("$lh", p.ListenHost);
        cmd.Parameters.AddWithValue("$lp", p.ListenPort);
        cmd.Parameters.AddWithValue("$ci", p.ConnectIp);
        cmd.Parameters.AddWithValue("$cp", p.ConnectPort);
        cmd.Parameters.AddWithValue("$sni", p.FakeSni);
        cmd.Parameters.AddWithValue("$act", p.IsActive ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private static SpoofProfile Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Name = r.GetString(1),
        ListenHost = r.GetString(2),
        ListenPort = (int)r.GetInt64(3),
        ConnectIp = r.GetString(4),
        ConnectPort = (int)r.GetInt64(5),
        FakeSni = r.GetString(6),
        IsActive = r.GetInt64(7) == 1,
    };
}
