using Microsoft.Data.Sqlite;
using SpoofGUI.Core;

namespace SpoofGUI.Database;

public sealed class DatabaseConnection
{
    private readonly string _connectionString;

    public DatabaseConnection()
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return c;
    }
}
