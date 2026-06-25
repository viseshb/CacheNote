using CacheNote.Core.Ai;

namespace CacheNote.Tests;

/// <summary>Pure-logic tests for the LLM response cache: hit/miss, LRU eviction, TTL expiry, and
/// scope/prompt sensitivity of the key.</summary>
public sealed class LlmResponseCacheTests
{
    [Fact]
    public void Set_Then_Get_ReturnsHit()
    {
        var cache = new LlmResponseCache();
        var key = LlmResponseCache.Key("gemini|flash", "sys", "user", null);

        cache.Set(key, "answer");

        Assert.True(cache.TryGet(key, out var value));
        Assert.Equal("answer", value);
    }

    [Fact]
    public void Get_Miss_ReturnsFalse()
    {
        var cache = new LlmResponseCache();
        Assert.False(cache.TryGet("nope", out var value));
        Assert.Equal("", value);
    }

    [Fact]
    public void Key_DiffersByScopeSystemUserAndSchema()
    {
        var baseKey = LlmResponseCache.Key("gemini|flash", "sys", "user", null);
        Assert.NotEqual(baseKey, LlmResponseCache.Key("fake|flash", "sys", "user", null));   // scope
        Assert.NotEqual(baseKey, LlmResponseCache.Key("gemini|flash", "sys2", "user", null)); // system
        Assert.NotEqual(baseKey, LlmResponseCache.Key("gemini|flash", "sys", "user2", null)); // user
        Assert.NotEqual(baseKey, LlmResponseCache.Key("gemini|flash", "sys", "user", "{}"));  // schema
    }

    [Fact]
    public void Key_IsStableForSameInputs()
        => Assert.Equal(
            LlmResponseCache.Key("s", "a", "b", "c"),
            LlmResponseCache.Key("s", "a", "b", "c"));

    [Fact]
    public void Evicts_LeastRecentlyUsed_WhenOverCapacity()
    {
        var cache = new LlmResponseCache(capacity: 2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        // Touch "a" so "b" becomes least-recently used.
        Assert.True(cache.TryGet("a", out _));
        cache.Set("c", "3");   // over capacity → evict LRU ("b")

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Expired_Entries_AreNotReturned()
    {
        var now = DateTime.UtcNow;
        var cache = new LlmResponseCache(capacity: 8, ttl: TimeSpan.FromMinutes(5), now: () => now);
        cache.Set("k", "v");
        Assert.True(cache.TryGet("k", out _));

        now = now.AddMinutes(6);   // advance past the TTL
        Assert.False(cache.TryGet("k", out _));
        Assert.Equal(0, cache.Count);   // expired entry is dropped on access
    }
}
