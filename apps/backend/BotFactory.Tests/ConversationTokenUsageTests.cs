using BotFactory.Core.Services;

namespace BotFactory.Tests;

/// <summary>
/// The total a conversation shows in the history: the sum of what each call to the chat model
/// recorded. A conversation with nothing recorded has no total, which is not the same as zero.
/// </summary>
public class ConversationTokenUsageTests
{
    private static string Call(long prompt, long output, long cached = 0, long thoughts = 0, long? total = null) =>
        $$"""
        {"model":"m","call":1,"reported":true,"complete":true,"promptTokens":{{prompt}},"outputTokens":{{output}},"cachedTokens":{{cached}},"thoughtsTokens":{{thoughts}},"totalTokens":{{total ?? prompt + output + thoughts}},"cachedPercent":0}
        """;

    [Fact]
    public void Sum_AddsEveryCallOfTheConversation()
    {
        var usage = ConversationTokenUsage.Sum([Call(27_492, 22, cached: 0), Call(31_018, 100, cached: 25_000, thoughts: 10)]);

        Assert.NotNull(usage);
        Assert.Equal(2, usage.Calls);
        Assert.Equal(58_510, usage.PromptTokens);
        Assert.Equal(25_000, usage.CachedTokens);
        Assert.Equal(122, usage.OutputTokens);
        Assert.Equal(10, usage.ThoughtsTokens);
        Assert.Equal(58_510 + 122 + 10, usage.TotalTokens);
    }

    [Fact]
    public void Sum_UsesTheProvidersTotal_WhenItDiffersFromTheParts()
    {
        // The provider counts more than prompt + output + thoughts (tool-use prompt tokens, say).
        var usage = ConversationTokenUsage.Sum([Call(100, 10, total: 150)]);

        Assert.Equal(150, usage!.TotalTokens);
    }

    [Fact]
    public void Sum_WithoutATotal_FallsBackToTheParts()
    {
        const string data = """{"reported":true,"promptTokens":100,"outputTokens":10,"thoughtsTokens":5}""";

        Assert.Equal(115, ConversationTokenUsage.Sum([data])!.TotalTokens);
    }

    [Fact]
    public void Sum_SkipsACallTheProviderSentNoCountsFor_AndKeepsTheRest()
    {
        const string unreported = """{"reported":false,"promptTokens":null,"totalTokens":null}""";

        var usage = ConversationTokenUsage.Sum([unreported, Call(1_000, 50)]);

        Assert.Equal(1, usage!.Calls);
        Assert.Equal(1_050, usage.TotalTokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"reported":false}""")]
    [InlineData("""{"reported":true,"promptTokens":0,"outputTokens":0,"totalTokens":0}""")]
    public void Sum_WithNothingUsable_IsNull_NotZero(string? data)
    {
        Assert.Null(ConversationTokenUsage.Sum([data]));
    }

    [Fact]
    public void Sum_WithNoEvents_IsNull()
    {
        Assert.Null(ConversationTokenUsage.Sum([]));
    }

    [Fact]
    public void Sum_ABrokenPayload_DoesNotHideTheOthers()
    {
        var usage = ConversationTokenUsage.Sum(["{broken", Call(500, 20)]);

        Assert.Equal(520, usage!.TotalTokens);
    }

    [Fact]
    public void Sum_TokenCountsBeyondIntRange_DoNotOverflow()
    {
        var usage = ConversationTokenUsage.Sum([Call(2_000_000_000, 0), Call(2_000_000_000, 0)]);

        Assert.Equal(4_000_000_000, usage!.TotalTokens);
    }
}
