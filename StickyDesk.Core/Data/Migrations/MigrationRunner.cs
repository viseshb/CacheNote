using Dapper;
using Microsoft.Extensions.Logging;

namespace StickyDesk.Core.Data.Migrations;

/// <summary>
/// Applies versioned schema migrations using SQLite's <c>PRAGMA user_version</c>
/// as the bookkeeping mechanism. Each migration runs once, in order, inside a
/// transaction. Adding a new schema version = append to <see cref="Migrations"/>.
/// </summary>
public sealed class MigrationRunner
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<MigrationRunner> _log;

    // (version, sql) pairs applied in ascending order when version > user_version.
    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, SchemaV1.Sql),
        (2, SchemaV2.Sql),
        (3, SchemaV3.Sql),
    ];

    public MigrationRunner(IDbConnectionFactory factory, ILogger<MigrationRunner> log)
    {
        _factory = factory;
        _log = log;
    }

    /// <summary>Brings the database schema up to the latest known version.</summary>
    public void Run()
    {
        using var conn = _factory.Create();
        var current = conn.ExecuteScalar<long>("PRAGMA user_version;");

        foreach (var (version, sql) in Migrations.OrderBy(m => m.Version))
        {
            if (version <= current)
                continue;

            using var tx = conn.BeginTransaction();
            conn.Execute(sql, transaction: tx);
            // PRAGMA does not accept parameters; version is an int literal we control.
            conn.Execute($"PRAGMA user_version = {version};", transaction: tx);
            tx.Commit();

            _log.LogInformation("Applied schema migration v{Version}.", version);
        }
    }

    /// <summary>Current on-disk schema version (0 = fresh).</summary>
    public long CurrentVersion()
    {
        using var conn = _factory.Create();
        return conn.ExecuteScalar<long>("PRAGMA user_version;");
    }

    /// <summary>Count of user tables — used by the M0 gate as proof the schema built.</summary>
    public int UserTableCount()
    {
        using var conn = _factory.Create();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';");
    }
}
