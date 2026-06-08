using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using CacheNote.Core.Data;
using CacheNote.Core.Data.Migrations;
using CacheNote.Core.Infrastructure;
using CacheNote.Core.Services;

namespace CacheNote.Core.DependencyInjection;

/// <summary>Registers all UI-agnostic CacheNote services (data, settings, paths).</summary>
public static class CoreServiceCollectionExtensions
{
    private static bool _dapperConfigured;

    public static IServiceCollection AddCacheNoteCore(this IServiceCollection services)
    {
        ConfigureDapper();

        services.AddSingleton<IAppPaths>(_ => new AppPaths());
        services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<IChecklistRepository, ChecklistRepository>();
        services.AddSingleton<IReminderRepository, ReminderRepository>();
        services.AddSingleton<ITaskRepository, TaskRepository>();
        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IEventRepository, EventRepository>();
        services.AddSingleton<EventService>();
        services.AddSingleton<IMdBlockRepository, MdBlockRepository>();
        services.AddSingleton<IReminderService, ReminderService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();

        // Cloud (M5 STT / M8 AI): config just reads .env; providers are created lazily per use,
        // so nothing network/audio/native is constructed at startup.
        services.AddSingleton<Cloud.CloudConfig>();
        services.AddSingleton<Cloud.CloudConnectivity>();
        services.AddSingleton<Speech.ISpeechToTextFactory, Speech.SpeechToTextFactory>();
        services.AddSingleton<Ai.IGeminiClientFactory, Ai.GeminiClientFactory>();
        services.AddSingleton<Ai.AiActionExecutor>();
        services.AddSingleton<Ai.AiAssistService>();

        services.AddTransient<ViewModels.NotesViewModel>();
        services.AddTransient<ViewModels.RemindersViewModel>();
        services.AddTransient<ViewModels.TasksViewModel>();
        services.AddTransient<ViewModels.FavoritesViewModel>();
        services.AddTransient<ViewModels.CalendarViewModel>();
        return services;
    }

    /// <summary>Global Dapper config. Idempotent so tests that build the container repeatedly are safe.</summary>
    public static void ConfigureDapper()
    {
        if (_dapperConfigured)
            return;
        _dapperConfigured = true;

        // Map snake_case columns (content_plain) to PascalCase props (ContentPlain).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Store every DateTime as round-trippable ISO-8601 UTC and read it back AS UTC,
        // so reminder time comparisons in C# are correct (SQLite/Dapper otherwise parse
        // a trailing 'Z' into a local-kind DateTime and shift the value).
        SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
    }

    private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
            => parameter.Value = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        public override DateTime Parse(object value)
            => DateTime.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                       .ToUniversalTime();
    }
}
