using System.Security.Cryptography;
using System.Text;

namespace CacheNote.Core.Ai;

/// <summary>
/// Thread-safe LRU + TTL cache for LLM completions. Memoizes identical (scope, system, user,
/// schema) prompts so repeated read-only calls — summarize the same note, rephrase the same text —
/// skip the network round-trip entirely (faster + no token spend). In-memory only: the cache is
/// rebuilt each launch, which is fine for a desktop session.
/// </summary>
public sealed class LlmResponseCache
{
    private sealed class Entry
    {
        public required string Key;
        public required string Value;
        public DateTime ExpiresUtc;
    }

    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _lru = new();   // First = most-recently used.

    public LlmResponseCache(int capacity = 200, TimeSpan? ttl = null, Func<DateTime>? now = null)
    {
        _capacity = capacity < 1 ? 1 : capacity;
        _ttl = ttl ?? TimeSpan.FromHours(6);
        _now = now ?? (() => DateTime.UtcNow);
        _map = new Dictionary<string, LinkedListNode<Entry>>(_capacity);
    }

    /// <summary>Stable key over the prompt inputs. NUL separators can't appear in prompt text, so
    /// distinct inputs never collide into the same key.</summary>
    public static string Key(string scope, string systemPrompt, string userPrompt, string? jsonSchema)
    {
        var raw = scope + "\0" + systemPrompt + "\0" + userPrompt + "\0" + (jsonSchema ?? "");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public bool TryGet(string key, out string value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (node.Value.ExpiresUtc <= _now())
                {
                    _lru.Remove(node);
                    _map.Remove(key);
                }
                else
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);          // Touch: promote to most-recent.
                    value = node.Value.Value;
                    return true;
                }
            }
        }
        value = "";
        return false;
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            var expires = _now() + _ttl;
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value.Value = value;
                existing.Value.ExpiresUtc = expires;
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<Entry>(new Entry { Key = key, Value = value, ExpiresUtc = expires });
            _lru.AddFirst(node);
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var oldest = _lru.Last!;          // Evict least-recently used.
                _lru.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }
        }
    }

    public int Count
    {
        get { lock (_gate) { return _map.Count; } }
    }
}
