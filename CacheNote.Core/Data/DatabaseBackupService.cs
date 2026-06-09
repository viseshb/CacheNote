using System.IO;
using Dapper;
using Microsoft.Extensions.Logging;
using CacheNote.Core.Infrastructure;

namespace CacheNote.Core.Data;

/// <summary>
/// Automatic SQLite backups so user data can never be lost to a bad write, a botched
/// update, or an accidental delete. Once per day (first launch of the day) the live
/// database is snapshotted atomically with <c>VACUUM INTO</c> — which includes everything
/// in the WAL — to <c>data\backups\CacheNote-yyyyMMdd.db</c>; the newest
/// <see cref="KeepCount"/> snapshots are kept.
/// </summary>
public sealed class DatabaseBackupService
{
    public const int KeepCount = 14;

    private readonly IDbConnectionFactory _factory;
    private readonly IAppPaths _paths;
    private readonly ILogger<DatabaseBackupService> _log;

    public DatabaseBackupService(IDbConnectionFactory factory, IAppPaths paths, ILogger<DatabaseBackupService> log)
    {
        _factory = factory;
        _paths = paths;
        _log = log;
    }

    public string BackupsDir => Path.Combine(_paths.DataDir, "backups");

    /// <summary>Take today's snapshot if it doesn't exist yet, then prune old ones. Never throws.</summary>
    public void RunDailyBackup()
    {
        try
        {
            Directory.CreateDirectory(BackupsDir);
            var target = Path.Combine(BackupsDir, $"CacheNote-{DateTime.Now:yyyyMMdd}.db");
            if (!File.Exists(target))
            {
                using var conn = _factory.Create();
                // VACUUM INTO writes a consistent, compacted snapshot (WAL included) and
                // fails rather than corrupting if anything goes wrong mid-write.
                conn.Execute($"VACUUM INTO '{target.Replace("'", "''")}';");
                _log.LogInformation("Database backup written: {Target}", target);
            }
            Prune();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Daily database backup failed (app continues normally).");
        }
    }

    private void Prune()
    {
        var old = new DirectoryInfo(BackupsDir)
            .GetFiles("CacheNote-*.db")
            .OrderByDescending(f => f.Name)
            .Skip(KeepCount);
        foreach (var f in old)
        {
            try { f.Delete(); } catch { /* locked/readonly — retry next day */ }
        }
    }
}
