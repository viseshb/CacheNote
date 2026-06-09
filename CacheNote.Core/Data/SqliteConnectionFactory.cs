using Microsoft.Data.Sqlite;
using CacheNote.Core.Infrastructure;

namespace CacheNote.Core.Data;

/// <summary>Creates opened SQLite connections to the CacheNote database.</summary>
public interface IDbConnectionFactory
{
    /// <summary>Returns a freshly opened connection with WAL + FK pragmas applied.</summary>
    SqliteConnection Create();
}

/// <inheritdoc cref="IDbConnectionFactory"/>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IAppPaths paths)
    {
        // Ensure the data directory exists before anyone tries to open the file.
        paths.EnsureCreated();

        // No Shared cache: shared-cache mode uses table-level locks that surface as
        // SQLITE_LOCKED (which busy_timeout does NOT retry) and defeats WAL's
        // reader/writer concurrency. Private cache + WAL + busy_timeout is the
        // standard pairing.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    public SqliteConnection Create()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // WAL keeps reads responsive while writes happen; NORMAL balances
        // durability/speed; busy_timeout avoids transient "database is locked".
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "PRAGMA journal_mode=WAL;" +
            "PRAGMA synchronous=NORMAL;" +
            "PRAGMA foreign_keys=ON;" +
            "PRAGMA busy_timeout=3000;";
        cmd.ExecuteNonQuery();

        return conn;
    }
}
