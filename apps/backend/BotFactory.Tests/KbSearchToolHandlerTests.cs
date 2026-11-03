using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Services;
using BotFactory.Infrastructure.Tools;

namespace BotFactory.Tests;

public class KbSearchToolHandlerTests
{
    private sealed class StubKbSearch : IKbSearchService
    {
        public List<KbSearchResult> Results { get; set; } = [];
        public (int BotId, string Query, int TopK, double Threshold)? LastCall { get; private set; }

        public Task<List<KbSearchResult>> SearchAsync(
            int botId, string query, int topK, double maxDistance, CancellationToken ct = default)
        {
            LastCall = (botId, query, topK, maxDistance);
            return Task.FromResult(Results);
        }
    }

    private static ToolContext Context() => new(
        new Bot
        {
            Id = 7,
            BotName = "Kb Bot",
            UrlSlug = "kb-bot",
            MasterPrompt = "x",
            TopK = 3,
            SimilarityThreshold = 0.42,
        },
        new Conversation { Id = 1, BotId = 7, UserId = "u" },
        TurnId: Guid.NewGuid());

    [Fact]
    public async Task ExecuteAsync_MapsResultsToModelPayloadAndDisplay()
    {
        var stub = new StubKbSearch
        {
            Results =
            [
                new KbSearchResult(11, 5, 0, "chunk text", "char:10-20", "manual.pdf", 0.12),
            ],
        };
        var handler = new KbSearchToolHandler(stub);
        var args = JsonDocument.Parse("""{"query":"¿Cuál es el trámite?"}""").RootElement;

        var invocation = await handler.ExecuteAsync(args, Context(), CancellationToken.None);

        Assert.Equal("kb_retrieved", invocation.Telemetry?.EventName);
        Assert.False(invocation.NoUsableResults);
        Assert.Single(invocation.Display);
        Assert.Equal("manual.pdf:10-20", invocation.Display[0].Label);
        Assert.Equal("chunk text", invocation.Display[0].Body);
        Assert.Equal((7, "¿Cuál es el trámite?", 3, 0.42), stub.LastCall);
        Assert.Equal("11", invocation.Display[0].Id);
        Assert.Contains("1 result", invocation.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_NoResults_SetsNoUsableResults()
    {
        var handler = new KbSearchToolHandler(new StubKbSearch());
        var args = JsonDocument.Parse("""{"query":"nada"}""").RootElement;

        var invocation = await handler.ExecuteAsync(args, Context(), CancellationToken.None);

        Assert.True(invocation.NoUsableResults);
        Assert.Empty(invocation.Display);
    }

    [Fact]
    public async Task ExecuteAsync_MissingQuery_UsesEmptyString()
    {
        var stub = new StubKbSearch();
        var handler = new KbSearchToolHandler(stub);
        var args = JsonDocument.Parse("{}").RootElement;

        await handler.ExecuteAsync(args, Context(), CancellationToken.None);

        Assert.Equal("", stub.LastCall?.Query);
    }

    [Fact]
    public void Declaration_UsesReservedToolName()
    {
        var handler = new KbSearchToolHandler(new StubKbSearch());
        Assert.Equal(KbSearchToolHandler.ToolName, handler.Name);
        Assert.Equal(KbSearchToolHandler.ToolName, handler.Declaration.Name);
        Assert.Contains("query", handler.Declaration.Parameters.Required);
    }
}
