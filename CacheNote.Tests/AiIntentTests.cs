using CacheNote.Core.Ai;

namespace CacheNote.Tests;

/// <summary>The intent heuristics route a chat message to the chat-only path, a clarifying question,
/// or the agentic planner. They're best-effort, but the routing contract they encode is worth pinning.</summary>
public sealed class AiIntentTests
{
    [Theory]
    [InlineData("what tasks do I have today?")]
    [InlineData("show me my tasks")]
    [InlineData("show me my reminders")]   // "reminders" (noun) must not trip the "remind" verb
    [InlineData("list my reminders")]
    [InlineData("summarize this note")]
    [InlineData("explain how this works")]
    public void IsReadOnly_True_ForQuestionsAndLookups(string text)
        => Assert.True(AiIntent.IsReadOnlyRequest(text));

    [Theory]
    [InlineData("remind me to call mom")]   // "remind" as a whole word is still an action
    [InlineData("create a reminder")]
    public void IsReadOnly_False_ForReminderActions(string text)
        => Assert.False(AiIntent.IsReadOnlyRequest(text));

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
