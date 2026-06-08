namespace CacheNote.Core.Ai;

/// <summary>
/// A minimal text-in/text-out LLM client. When <paramref name="jsonSchema"/> is supplied the model
/// is asked to return JSON matching it (used for the agentic action list).
/// </summary>
public interface IGeminiClient
{
    bool IsConfigured { get; }
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, string? jsonSchema = null, CancellationToken ct = default);
}

/// <summary>Creates the configured <see cref="IGeminiClient"/> (vertex | gemini | fake).</summary>
public interface IGeminiClientFactory
{
    string Provider { get; }
    IGeminiClient Create();
}
