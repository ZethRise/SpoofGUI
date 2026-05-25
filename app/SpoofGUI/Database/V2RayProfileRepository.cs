using Microsoft.Data.Sqlite;
using SpoofGUI.Models;

namespace SpoofGUI.Database;

public sealed class V2RayProfileRepository
{
    private readonly DatabaseConnection _db;
    public V2RayProfileRepository(DatabaseConnection db) => _db = db;

    public IReadOnlyList<V2RayProfile> All()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, protocol, mode, address, port, user_id, security, transport, server_name, raw_uri
FROM v2ray_profiles ORDER BY updated_at DESC, id DESC;";
        var list = new List<V2RayProfile>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public void Upsert(V2RayProfile p)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO v2ray_profiles (id, name, protocol, mode, address, port, user_id, security, transport, server_name, raw_uri, updated_at)
VALUES ($id, $name, $protocol, $mode, $address, $port, $user_id, $security, $transport, $server_name, $raw_uri, datetime('now'))
ON CONFLICT(id) DO UPDATE SET
    name=excluded.name,
    protocol=excluded.protocol,
    mode=excluded.mode,
    address=excluded.address,
    port=excluded.port,
    user_id=excluded.user_id,
    security=excluded.security,
    transport=excluded.transport,
    server_name=excluded.server_name,
    raw_uri=excluded.raw_uri,
    updated_at=datetime('now');
SELECT CASE WHEN $id IS NULL THEN last_insert_rowid() ELSE $id END;";
        cmd.Parameters.AddWithValue("$id", p.Id == 0 ? DBNull.Value : p.Id);
        cmd.Parameters.AddWithValue("$name", p.Name);
        cmd.Parameters.AddWithValue("$protocol", p.Protocol);
        cmd.Parameters.AddWithValue("$mode", p.Mode);
        cmd.Parameters.AddWithValue("$address", p.Address);
        cmd.Parameters.AddWithValue("$port", p.Port);
        cmd.Parameters.AddWithValue("$user_id", p.UserId);
        cmd.Parameters.AddWithValue("$security", p.Security);
        cmd.Parameters.AddWithValue("$transport", p.Transport);
        cmd.Parameters.AddWithValue("$server_name", p.ServerName);
        cmd.Parameters.AddWithValue("$raw_uri", p.RawUri);
        var result = cmd.ExecuteScalar();
        if (result is long id) p.Id = id;
    }

    public void Delete(long id)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM v2ray_profiles WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static V2RayProfile Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Name = r.GetString(1),
        Protocol = r.GetString(2),
        Mode = r.GetString(3),
        Address = r.GetString(4),
        Port = (int)r.GetInt64(5),
        UserId = r.GetString(6),
        Security = r.GetString(7),
        Transport = r.GetString(8),
        ServerName = r.GetString(9),
        RawUri = r.GetString(10),
    };
}
