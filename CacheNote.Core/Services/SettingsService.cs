using System.Globalization;
using Dapper;
using CacheNote.Core.Data;

namespace CacheNote.Core.Services;

/// <summary>
/// Persistent key/value application settings backed by the <c>settings</c> table
/// (theme, window state, always-on-top, pause-notifications, etc.). Stored in the
/// database so it travels with the rest of the user's data inside the app folder.
/// </summary>
public interface ISettingsService
{
    string? Get(string key);
    string Get(string key, string fallback);
    bool GetBool(string key, bool fallback = false);
    int GetInt(string key, int fallback = 0);
    void Set(string key, string? value);
    void SetBool(string key, bool value);
    void SetInt(string key, int value);
}

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService : ISettingsService
{
    private readonly IDbConnectionFactory _factory;

    public SettingsService(IDbConnectionFactory factory) => _factory = factory;

    public string? Get(string key)
    {
        using var conn = _factory.Create();
        return conn.ExecuteScalar<string?>(
            "SELECT value FROM settings WHERE key = @key;", new { key });
    }

    public string Get(string key, string fallback) => Get(key) ?? fallback;

    public bool GetBool(string key, bool fallback = false) =>
        bool.TryParse(Get(key), out var v) ? v : fallback;

    public int GetInt(string key, int fallback = 0) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public void Set(string key, string? value)
    {
        using var conn = _factory.Create();
        conn.Execute(
            """
            INSERT INTO settings(key, value, updated_utc)
            VALUES (@key, @value, @now)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc;
            """,
            new { key, value, now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
    }

    public void SetBool(string key, bool value) =>
        Set(key, value ? "true" : "false");

    public void SetInt(string key, int value) =>
        Set(key, value.ToString(CultureInfo.InvariantCulture));
}
