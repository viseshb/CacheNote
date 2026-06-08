namespace CacheNote.Core.Infrastructure;

/// <summary>
/// Single source of truth for every on-disk location CacheNote uses.
/// Invariant: all user data lives in the application root folder (the folder
/// containing the exe) in BOTH installed and portable modes. The install
/// location is user-writable, so this honors the PRD requirement that "all
/// user data remains inside the application folder" and that moving the
/// folder preserves everything.
/// </summary>
public interface IAppPaths
{
    /// <summary>Application root = the exe's own folder.</summary>
    string Root { get; }
    string DataDir { get; }
    string AttachmentsDir { get; }
    string ConfigDir { get; }
    string LogsDir { get; }
    string DatabaseFile { get; }
    string SettingsFile { get; }
    string LogFile { get; }

    /// <summary>True when a ".portable" marker file sits next to the exe.</summary>
    bool IsPortable { get; }

    /// <summary>Creates every data directory if missing. Idempotent.</summary>
    void EnsureCreated();
}

/// <inheritdoc cref="IAppPaths"/>
public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = (rootOverride ?? AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        DataDir = Path.Combine(Root, "data");
        AttachmentsDir = Path.Combine(Root, "attachments");
        ConfigDir = Path.Combine(Root, "config");
        LogsDir = Path.Combine(Root, "logs");

        DatabaseFile = Path.Combine(DataDir, "CacheNote.db");
        SettingsFile = Path.Combine(ConfigDir, "settings.json");
        LogFile = Path.Combine(LogsDir, "app.log");
    }

    public string Root { get; }
    public string DataDir { get; }
    public string AttachmentsDir { get; }
    public string ConfigDir { get; }
    public string LogsDir { get; }
    public string DatabaseFile { get; }
    public string SettingsFile { get; }
    public string LogFile { get; }

    public bool IsPortable => File.Exists(Path.Combine(Root, ".portable"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(AttachmentsDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogsDir);
    }
}
