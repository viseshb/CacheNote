using CacheNote.Core.Ai;
using CacheNote.Core.Cloud;
using CacheNote.Core.Infrastructure;

namespace CacheNote.Tests;

/// <summary>The caching decorator must memoize read-only completions (jsonSchema == null) and NEVER
/// cache agentic/plan completions (jsonSchema != null), whose result is applied as a side effect.</summary>
public sealed class CachingGeminiClientTests
{
    private sealed class CountingClient : IGeminiClient
    {
        public int Calls;
        public bool IsConfigured => true;
        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, string? jsonSchema = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult("result-" + Calls);
        }
    }

    [Fact]
    public async Task ReadOnlyCall_IsCached_SecondCallSkipsInner()
    {
        var inner = new CountingClient();
        var client = new CachingGeminiClient(inner, new LlmResponseCache(), "scope");

        var a = await client.CompleteAsync("sys", "summarize this", jsonSchema: null);
        var b = await client.CompleteAsync("sys", "summarize this", jsonSchema: null);

        Assert.Equal("result-1", a);
        Assert.Equal("result-1", b);   // served from cache
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task AgenticCall_IsNeverCached()
    {
        var inner = new CountingClient();
        var client = new CachingGeminiClient(inner, new LlmResponseCache(), "scope");

        await client.CompleteAsync("sys", "plan this", jsonSchema: "{}");
        await client.CompleteAsync("sys", "plan this", jsonSchema: "{}");

        Assert.Equal(2, inner.Calls);   // both hit the inner client — no replay of side effects
    }

    [Fact]
    public async Task DifferentScope_DoesNotShareCache()
    {
        var inner = new CountingClient();
        var cache = new LlmResponseCache();

        var first = await new CachingGeminiClient(inner, cache, "gemini|flash").CompleteAsync("s", "u", null);
        var second = await new CachingGeminiClient(inner, cache, "fake|flash").CompleteAsync("s", "u", null);

        Assert.Equal("result-1", first);
        Assert.Equal("result-2", second);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task BlankResult_IsNotCached()
    {
        var blank = new BlankClient();
        var client = new CachingGeminiClient(blank, new LlmResponseCache(), "scope");

        await client.CompleteAsync("s", "u", null);
        await client.CompleteAsync("s", "u", null);

        Assert.Equal(2, blank.Calls);   // blank completions are a transient miss, not an answer
    }

    private sealed class BlankClient : IGeminiClient
    {
        public int Calls;
        public bool IsConfigured => true;
        public Task<string> CompleteAsync(string s, string u, string? j = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult("   ");
        }
    }

    // Proves caching through the EXACT production wiring: GeminiClientFactory wraps each Create()
    // in a CachingGeminiClient sharing one cache, so a repeated read-only prompt across separate
    // Create() calls hits the inner client only once.
    [Fact]
    public async Task Factory_SharesCacheAcrossCreateCalls()
    {
        var root = Path.Combine(Path.GetTempPath(), "CacheNote-tests", Guid.NewGuid().ToString("N"));
        var cfg = new CloudConfig(new AppPaths(root));
        var inner = new CountingClient();
        var factory = new GeminiClientFactory(cfg, _ => inner, new LlmResponseCache());

        var r1 = await factory.Create().CompleteAsync("sys", "same prompt", jsonSchema: null);
        var r2 = await factory.Create().CompleteAsync("sys", "same prompt", jsonSchema: null);
        var r3 = await factory.Create().CompleteAsync("sys", "different prompt", jsonSchema: null);

        Assert.Equal("result-1", r1);
        Assert.Equal("result-1", r2);   // cached across a fresh Create()
        Assert.Equal("result-2", r3);   // new prompt → new call
        Assert.Equal(2, inner.Calls);
    }
}
