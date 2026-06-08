using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using StickyDesk.Core.Data;
using StickyDesk.Core.Data.Migrations;
using StickyDesk.Core.Infrastructure;

namespace StickyDesk.Tests;

public sealed class MigrationsTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly MigrationRunner _runner;
    private readonly SqliteConnectionFactory _factory;

    public MigrationsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "stickydesk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new AppPaths(_root);
        _factory = new SqliteConnectionFactory(_paths);
        _runner = new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance);
    }

    [Fact]
    public void Run_CreatesSchema()
    {
        _runner.Run();

        Assert.Equal(3, _runner.CurrentVersion());
        Assert.True(File.Exists(_paths.DatabaseFile));

        using var conn = _factory.Create();
        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';")
            .ToHashSet();

        foreach (var expected in new[]
                 {
                     "notes", "checklist_items", "tasks", "reminders",
                     "tags", "note_tags", "attachments", "settings", "notes_fts",
                     "events",
                 })
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public void Run_IsIdempotent()
    {
        _runner.Run();
        _runner.Run(); // must not throw or duplicate
        Assert.Equal(3, _runner.CurrentVersion());
    }

    [Fact]
    public void Fts_StaysInSyncViaTriggers()
    {
        _runner.Run();
        using var conn = _factory.Create();

        conn.Execute(
            "INSERT INTO notes(title, content_plain, created_utc, updated_utc) VALUES (@t,@c,@n,@n);",
            new { t = "Groceries", c = "milk eggs bread coffee", n = DateTime.UtcNow.ToString("O") });

        var hits = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM notes_fts WHERE notes_fts MATCH 'coffee';");
        Assert.Equal(1, hits);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
