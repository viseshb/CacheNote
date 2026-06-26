using CacheNote.Core.Ai;

namespace CacheNote.Tests;

/// <summary>The model sometimes wraps a rephrase in fences, "Here is the rewritten text:" prefixes,
/// or quotes. CleanRephrase strips those so only the rewritten text lands back in the note.</summary>
public sealed class RephraseCleanTests
{
    [Fact]
    public void StripsKnownPrefix()
        => Assert.Equal("The cat sat.", AiAssistService.CleanRephrase("Here is the rewritten text: The cat sat.", "x"));

    [Fact]
    public void StripsWrappingQuotes()
        => Assert.Equal("Hello world", AiAssistService.CleanRephrase("\"Hello world\"", "x"));

    [Fact]
    public void StripsCodeFences()
        => Assert.Equal("plain line", AiAssistService.CleanRephrase("```\nplain line\n```", "x"));

    [Fact]
    public void StripsPrefixOnItsOwnLine()
        => Assert.Equal("Body text here.", AiAssistService.CleanRephrase("Here's the rephrased text:\nBody text here.", "x"));

    [Fact]
    public void LeavesCleanTextUntouched()
        => Assert.Equal("Already clean.", AiAssistService.CleanRephrase("Already clean.", "x"));

    [Fact]
    public void FallsBackToOriginal_WhenCleaningEmptiesIt()
        => Assert.Equal("original", AiAssistService.CleanRephrase("\"\"", "original"));
}
