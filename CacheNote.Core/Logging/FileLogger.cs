using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheNote.Core.Logging;

/// <summary>
/// Minimal dependency-free file logger that appends to <c>logs/app.log</c>.
/// Keeps the app self-contained (no Serilog/NReco). Thread-safe via a lock;
/// volume is low (a desktop notes app), so simple synchronous appends are fine.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly LogLevel _min;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string path, LogLevel min)
    {
        _path = path;
        _min = min;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        if (level < _min)
            return;

        var sb = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(Short(level)).Append("] ")
            .Append(category).Append(" - ").Append(message);

        if (ex is not null)
            sb.AppendLine().Append(ex);

        sb.AppendLine();

        lock (_gate)
        {
            File.AppendAllText(_path, sb.ToString());
        }
    }

    internal LogLevel Min => _min;

    private static string Short(LogLevel l) => l switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "   ",
    };

    public void Dispose() => _loggers.Clear();

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider.Min;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}

/// <summary>Logging-builder helper to wire the file logger.</summary>
public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string path, LogLevel min = LogLevel.Information)
    {
        builder.Services.AddSingleton<ILoggerProvider>(new FileLoggerProvider(path, min));
        return builder;
    }
}
