using Microsoft.Data.Sqlite;
using StickyDesk.Core.Infrastructure;

namespace StickyDesk.Core.Data;

/// <summary>Creates opened SQLite connections to the StickyDesk database.</summary>
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

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
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
