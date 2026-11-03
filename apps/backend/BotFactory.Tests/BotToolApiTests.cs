using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class BotToolApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public BotToolApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private async Task<(int BotId, int ToolId)> SeedPublishedToolAsync(HttpClient client)
    {
        var connResp = await client.PostAsJsonAsync("/api/tool-connections", new CreateToolConnectionRequest(
            Name: $"bot-tool-conn-{Guid.NewGuid():N}",
            Description: null,
            ConnectorType: "Rest",
            BaseUrl: "https://bot-tools.example.test",
            AuthType: "None",
            AuthConfigJson: "{}",
            SecretsJson: null,
            DefaultHeadersJson: null,
            TimeoutMs: 8000,
            MaxResponseBytes: 262144));
        connResp.EnsureSuccessStatusCode();
        var conn = await connResp.Content.ReadFromJsonAsync<ToolConnectionResponse>();

        var toolName = $"bottool_{Guid.NewGuid():N}";
        var toolResp = await client.PostAsJsonAsync("/api/tools", new CreateToolDefinitionRequest(
            ToolConnectionId: conn!.Id,
            Name: toolName,
            DisplayName: "Bot Tool",
            Category: null,
            SupportContact: null,
            AiDescription: "Tool for bot association tests.",
            AiUsageNotes: null,
            HttpMethod: "GET",
            PathTemplate: "/health",
            QueryParamsJson: null,
            StaticHeadersJson: null,
            BodyTemplateJson: null,
            ParametersSchemaJson: """{"type":"object","properties":{}}""",
            ResponseMappingJson: null,
            ErrorMappingJson: null,
            NormalizeArgDiacritics: false,
            RequiresAuthenticatedUser: false,
            MaxCallsPerConversation: null,
            TimeoutMsOverride: null,
            DataClassification: "Public"));
        toolResp.EnsureSuccessStatusCode();
        var tool = await toolResp.Content.ReadFromJsonAsync<ToolDefinitionResponse>();
        await client.PostAsync($"/api/tools/{tool!.Id}/publish", null);

        var botResp = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Bot Tools Test Bot",
            urlSlug = $"bot-tools-{Guid.NewGuid():N}",
        });
        botResp.EnsureSuccessStatusCode();
        var bot = await botResp.Content.ReadFromJsonAsync<BotResponse>();

        return (bot!.Id, tool.Id);
    }

    [Fact]
    public async Task ListTools_IncludesPublishedTool_DisabledByDefault()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var (botId, toolId) = await SeedPublishedToolAsync(client);

        var list = await client.GetFromJsonAsync<List<BotToolResponse>>($"/api/bots/{botId}/tools");
        Assert.NotNull(list);
        var row = Assert.Single(list, t => t.ToolDefinitionId == toolId);
        Assert.Equal("Published", row.Status);
        Assert.False(row.Enabled);
    }

    [Fact]
    public async Task UpdateTools_EnablesAssociationWithInstructions()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var (botId, toolId) = await SeedPublishedToolAsync(client);

        var update = await client.PutAsJsonAsync($"/api/bots/{botId}/tools", new UpdateBotToolsRequest([
            new BotToolUpsert(toolId, true, "Use sparingly.", 3),
        ]));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var list = await client.GetFromJsonAsync<List<BotToolResponse>>($"/api/bots/{botId}/tools");
        Assert.NotNull(list);
        var row = Assert.Single(list, t => t.ToolDefinitionId == toolId);
        Assert.True(row.Enabled);
        Assert.Equal("Use sparingly.", row.AdditionalInstructions);
        Assert.Equal(3, row.MaxCallsPerConversationOverride);
    }

    [Fact]
    public async Task UpdateTools_UnknownToolId_Returns400()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botResp = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Empty Tools Bot",
            urlSlug = $"empty-tools-{Guid.NewGuid():N}",
        });
        botResp.EnsureSuccessStatusCode();
        var bot = await botResp.Content.ReadFromJsonAsync<BotResponse>();

        var update = await client.PutAsJsonAsync($"/api/bots/{bot!.Id}/tools", new UpdateBotToolsRequest([
            new BotToolUpsert(999_999, true, null, null),
        ]));
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task ChatOnlyUser_ForbiddenOnUpdateBotTools()
    {
        var admin = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var (botId, toolId) = await SeedPublishedToolAsync(admin);

        var chatOnly = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "public@example.com");
        var update = await chatOnly.PutAsJsonAsync($"/api/bots/{botId}/tools", new UpdateBotToolsRequest([
            new BotToolUpsert(toolId, true, null, null),
        ]));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }
}
