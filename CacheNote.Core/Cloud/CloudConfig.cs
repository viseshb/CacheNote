using System.IO;
using CacheNote.Core.Infrastructure;

namespace CacheNote.Core.Cloud;

/// <summary>
/// Loads cloud secrets/config from a <c>.env</c> file at the app root (offline-first, portable).
/// A real environment variable of the same name overrides the file (handy for tests + CI).
/// Keys are returned raw to callers but never logged — call <see cref="Mask"/> before logging.
/// </summary>
public sealed class CloudConfig
{
    private readonly Dictionary<string, string> _file;

    public CloudConfig(IAppPaths paths)
    {
        _file = Load(Path.Combine(paths.Root, ".env"));
    }

    public string Get(string key, string fallback = "")
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();
        return _file.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
    }

    // ----- Speech-to-text (M5) -----
    public string SttProvider => Get("STT_PROVIDER", "deepgram").ToLowerInvariant();
    public string DeepgramKey => Get("DEEPGRAM_API_KEY");
    public string DeepgramModel => Get("DEEPGRAM_MODEL", "nova-3");
    public string AssemblyAiKey => Get("ASSEMBLYAI_API_KEY");

    // ----- AI / Gemini (M8) -----
    public string VertexKey => Get("VERTEX_AI_API_KEY");
    public string VertexBaseUrl => Get("VERTEX_BASE_URL", "https://aiplatform.googleapis.com/v1");
    public string GeminiKey => Get("GEMINI_API_KEY");
    public string GeminiBaseUrl => Get("GEMINI_BASE_URL", "https://generativelanguage.googleapis.com/v1beta");
    public string GeminiModel => Get("GEMINI_MODEL", "gemini-3.5-flash");

    // ----- Auto-update (M10) -----
    // Baked-in defaults (not secrets) so auto-update works in every shipped build, not just dev
    // .env. A .env entry still overrides for forks/testing.
    public string GithubOwner => Get("GITHUB_OWNER", "viseshb");
    public string GithubRepo => Get("GITHUB_REPO", "CacheNote");

    // ----- Google Calendar sync (V2; needs a Google Cloud OAuth Desktop client) -----
    public string GoogleClientId => Get("GOOGLE_CLIENT_ID");
    public string GoogleClientSecret => Get("GOOGLE_CLIENT_SECRET");
    public bool GoogleSyncConfigured => !string.IsNullOrWhiteSpace(GoogleClientId);

    /// <summary>vertex | gemini | fake — explicit override, else inferred from which key is present.</summary>
    public string AiProvider
    {
        get
        {
            var explicitProvider = Get("AI_PROVIDER").ToLowerInvariant();
            if (!string.IsNullOrEmpty(explicitProvider))
                return explicitProvider;
            if (!string.IsNullOrWhiteSpace(VertexKey))
                return "vertex";
            if (!string.IsNullOrWhiteSpace(GeminiKey))
                return "gemini";
            return "none";
        }
    }

    /// <summary>Redact a secret for logging (keeps the last 4 chars).</summary>
    public static string Mask(string? secret)
        => string.IsNullOrEmpty(secret) ? "(none)" : new string('•', Math.Max(0, secret.Length - 4)) + secret[^Math.Min(4, secret.Length)..];

    private static Dictionary<string, string> Load(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return map;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (val.Length >= 2 && ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
                val = val[1..^1];
            map[key] = val;
        }
        return map;
    }
}
