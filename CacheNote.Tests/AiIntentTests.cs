using CacheNote.Core.Ai;

namespace CacheNote.Tests;

/// <summary>The intent heuristics route a chat message to the chat-only path, a clarifying question,
/// or the agentic planner. They're best-effort, but the routing contract they encode is worth pinning.</summary>
public sealed class AiIntentTests
{
    [Theory]
    [InlineData("what tasks do I have today?")]
    [InlineData("show me my tasks")]
    [InlineData("summarize this note")]
    [InlineData("explain how this works")]
    public void IsReadOnly_True_ForQuestionsAndLookups(string text)
        => Assert.True(AiIntent.IsReadOnlyRequest(text));

    // Known heuristic limitation: "reminders" contains the action verb "remind", so a read-only
    // "show me my reminders" routes to the planner instead of the chat-only path. It's still answered
    // safely (the agent returns empty actions for questions); pinned here so the behavior is explicit.
    [Fact]
    public void IsReadOnly_False_ForReminderLookup_KnownQuirk()
        => Assert.False(AiIntent.IsReadOnlyRequest("show me my reminders"));

    [Theory]
    [InlineData("create a task to call mom")]
    [InlineData("add a reminder for tomorrow")]
    [InlineData("delete this note")]
    [InlineData("pin the current note")]
    public void IsReadOnly_False_WhenAnActionVerbIsPresent(string text)
        => Assert.False(AiIntent.IsReadOnlyRequest(text));

    [Fact]
    public void IsBareCreate_True_ForVagueCreate()
        => Assert.True(AiIntent.IsBareCreateRequest("create a note", "note", "notes"));

    [Theory]
    [InlineData("create a note called Groceries")]          // has a title
    [InlineData("create a note about the meeting")]         // has a subject
    [InlineData("make a note with the budget for Q3 plan")] // detailed
    public void IsBareCreate_False_WhenSpecificsArePresent(string text)
        => Assert.False(AiIntent.IsBareCreateRequest(text, "note", "notes"));

    [Fact]
    public void IsBareCreate_False_WhenNounDoesNotMatch()
        => Assert.False(AiIntent.IsBareCreateRequest("create a task", "note", "notes"));

    [Theory]
    [InlineData("summarize this", true)]
    [InlineData("give me a recap", true)]
    [InlineData("rewrite this paragraph", false)]
    public void IsSummaryRequest_MatchesSummaryWords(string text, bool expected)
        => Assert.Equal(expected, AiIntent.IsSummaryRequest(text));

    [Theory]
    [InlineData("rephrase this sentence", true)]
    [InlineData("please improve wording", true)]
    [InlineData("what time is the meeting", false)]
    public void IsRephraseRequest_MatchesRephraseWords(string text, bool expected)
        => Assert.Equal(expected, AiIntent.IsRephraseRequest(text));
}
