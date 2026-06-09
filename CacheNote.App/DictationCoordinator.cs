using System;
using System.Threading.Tasks;

namespace CacheNote_App;

/// <summary>
/// Guarantees a single active speech-to-text session app-wide. Dictation now lives in two places —
/// the editor toolbar mic and the AI ball — and two <c>MicCaptureService</c> instances capturing at
/// once would double-feed the provider (duplicate text + double cost). Whoever starts dictation
/// claims the mic, stopping the previous owner first.
/// </summary>
internal static class DictationCoordinator
{
    private static object? _owner;
    private static Func<Task>? _stop;

    /// <summary>Claim the mic for <paramref name="owner"/>, stopping whoever held it.</summary>
    public static async Task ClaimAsync(object owner, Func<Task> stop)
    {
        var previousStop = _stop;
        var previousOwner = _owner;
        _owner = owner;
        _stop = stop;
        if (previousStop is not null && !ReferenceEquals(previousOwner, owner))
        {
            try { await previousStop(); } catch { /* best effort */ }
        }
    }

    /// <summary>Release the mic if <paramref name="owner"/> still holds it.</summary>
    public static void Release(object owner)
    {
        if (ReferenceEquals(_owner, owner))
        {
            _owner = null;
            _stop = null;
        }
    }
}
