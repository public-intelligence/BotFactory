using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class ToolDefinitionApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ToolDefinitionApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private async Task<ToolConnectionResponse> CreateConnectionAsync(HttpClient client)
    {
        var name = $"conn-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/tool-connections", new CreateToolConnectionRequest(
            Name: name,
            Description: null,
            ConnectorType: "Rest",
            BaseUrl: "https://tools.example.test",
            AuthType: "None",
            AuthConfigJson: "{}",
            SecretsJson: null,
            DefaultHeadersJson: null,
            TimeoutMs: 8000,
            MaxResponseBytes: 262144));
        response.EnsureSuccessStatusCode();
        var conn = await response.Content.ReadFromJsonAsync<ToolConnectionResponse>();
        return conn!;
    }

    private static CreateToolDefinitionRequest ValidToolRequest(int connectionId, string name) => new(
        ToolConnectionId: connectionId,
        Name: name,
        DisplayName: "Test Tool",
        Category: "Testing",
        SupportContact: null,
        AiDescription: "Runs a test lookup.",
        AiUsageNotes: null,
        HttpMethod: "GET",
        PathTemplate: "/items/{id}",
        QueryParamsJson: null,
        StaticHeadersJson: null,
        BodyTemplateJson: null,
        ParametersSchemaJson: """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"]}""",
        ResponseMappingJson: null,
        ErrorMappingJson: null,
        NormalizeArgDiacritics: false,
        RequiresAuthenticatedUser: true,
        MaxCallsPerConversation: null,
        TimeoutMsOverride: null,
        DataClassification: "Internal");

    [Fact]
    public async Task FullCrudLifecycle_HappyPath()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var conn = await CreateConnectionAsync(client);
        var toolName = $"lookup_{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/tools", ValidToolRequest(conn.Id, toolName));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ToolDefinitionResponse>();
        Assert.NotNull(created);
        Assert.Equal(toolName, created.Name);
        Assert.Equal("Draft", created.Status);

        var get = await client.GetFromJsonAsync<ToolDefinitionResponse>($"/api/tools/{created.Id}");
        Assert.NotNull(get);
        Assert.Equal(created.Id, get.Id);

        var list = await client.GetFromJsonAsync<PaginatedResponse<ToolDefinitionResponse>>("/api/tools?search=Test");
        Assert.NotNull(list);
        Assert.Contains(list.Items, t => t.Id == created.Id);

        var update = await client.PutAsJsonAsync($"/api/tools/{created.Id}", new UpdateToolDefinitionRequest(
            ToolConnectionId: null,
            Name: null,
            DisplayName: "Renamed Tool",
            Category: null,
            SupportContact: null,
            AiDescription: null,
            AiUsageNotes: null,
            HttpMethod: null,
            PathTemplate: null,
            QueryParamsJson: null,
            StaticHeadersJson: null,
            BodyTemplateJson: null,
            ParametersSchemaJson: null,
            ResponseMappingJson: null,
            ErrorMappingJson: null,
            NormalizeArgDiacritics: null,
            RequiresAuthenticatedUser: null,
            MaxCallsPerConversation: null,
            TimeoutMsOverride: null,
            DataClassification: null));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ToolDefinitionResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed Tool", updated.DisplayName);

        var delete = await client.DeleteAsync($"/api/tools/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task PublishAndUnpublish_ChangesStatus()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var conn = await CreateConnectionAsync(client);
        var toolName = $"pubtool_{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/tools", ValidToolRequest(conn.Id, toolName));
        create.EnsureSuccessStatusCode();
        var tool = await create.Content.ReadFromJsonAsync<ToolDefinitionResponse>();
        Assert.NotNull(tool);

        var publish = await client.PostAsync($"/api/tools/{tool.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var published = await client.GetFromJsonAsync<ToolDefinitionResponse>($"/api/tools/{tool.Id}");
        Assert.NotNull(published);
        Assert.Equal("Published", published.Status);

        var unpublish = await client.PostAsync($"/api/tools/{tool.Id}/unpublish", null);
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);

        var draft = await client.GetFromJsonAsync<ToolDefinitionResponse>($"/api/tools/{tool.Id}");
        Assert.NotNull(draft);
        Assert.Equal("Draft", draft.Status);
    }

    [Fact]
    public async Task Delete_WithBotAssociation_Returns409()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var conn = await CreateConnectionAsync(client);
        var toolName = $"assoc_{Guid.NewGuid():N}";

        var createTool = await client.PostAsJsonAsync("/api/tools", ValidToolRequest(conn.Id, toolName));
        createTool.EnsureSuccessStatusCode();
        var tool = await createTool.Content.ReadFromJsonAsync<ToolDefinitionResponse>();
        Assert.NotNull(tool);

        var publish = await client.PostAsync($"/api/tools/{tool.Id}/publish", null);
        publish.EnsureSuccessStatusCode();

        var createBot = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Tool Assoc Bot",
            urlSlug = $"tool-assoc-{Guid.NewGuid():N}",
        });
        createBot.EnsureSuccessStatusCode();
        var bot = await createBot.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(bot);

        var associate = await client.PutAsJsonAsync($"/api/bots/{bot.Id}/tools", new UpdateBotToolsRequest([
            new BotToolUpsert(tool.Id, true, null, null),
        ]));
        Assert.Equal(HttpStatusCode.NoContent, associate.StatusCode);

        var delete = await client.DeleteAsync($"/api/tools/{tool.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }
}
