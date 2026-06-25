namespace CacheNote.Core.Ai;

/// <summary>
/// Decorates an <see cref="IGeminiClient"/> with response caching. Only read-only completions
/// (jsonSchema == null: summaries, rephrases, chat answers) are cached — the agentic plan path
/// (jsonSchema != null) is never cached because its result is applied as a side effect and must
/// not be replayed, and because its key (full conversation + live app context + date) virtually
/// never repeats anyway. The <paramref name="scope"/> (provider|model) is part of the key so a
/// provider or model change never serves a stale answer.
/// </summary>
public sealed class CachingGeminiClient : IGeminiClient
{
    private readonly IGeminiClient _inner;
    private readonly LlmResponseCache _cache;
    private readonly string _scope;

    public CachingGeminiClient(IGeminiClient inner, LlmResponseCache cache, string scope)
    {
        _inner = inner;
        _cache = cache;
        _scope = scope;
    }

    public bool IsConfigured => _inner.IsConfigured;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, string? jsonSchema = null, CancellationToken ct = default)
    {
        // Never cache side-effecting (agentic) calls.
        if (jsonSchema is not null)
            return await _inner.CompleteAsync(systemPrompt, userPrompt, jsonSchema, ct);

        var key = LlmResponseCache.Key(_scope, systemPrompt, userPrompt, jsonSchema);
        if (_cache.TryGet(key, out var hit))
            return hit;

        var result = await _inner.CompleteAsync(systemPrompt, userPrompt, jsonSchema, ct);

        // Don't cache empty/blank completions — they're usually a transient miss, not an answer.
        if (!string.IsNullOrWhiteSpace(result))
            _cache.Set(key, result);

        return result;
    }
}
