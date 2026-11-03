using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class ToolConnectionApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ToolConnectionApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static CreateToolConnectionRequest ValidCreateRequest(string name) => new(
        Name: name,
        Description: "Test connection",
        ConnectorType: "Rest",
        BaseUrl: "https://api.example.test",
        AuthType: "None",
        AuthConfigJson: "{}",
        SecretsJson: null,
        DefaultHeadersJson: null,
        TimeoutMs: 8000,
        MaxResponseBytes: 262144);

    [Fact]
    public async Task FullCrudLifecycle_HappyPath()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var name = $"conn-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/tool-connections", ValidCreateRequest(name));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ToolConnectionResponse>();
        Assert.NotNull(created);
        Assert.Equal(name, created.Name);
        Assert.False(created.HasSecrets);

        var get = await client.GetFromJsonAsync<ToolConnectionResponse>($"/api/tool-connections/{created.Id}");
        Assert.NotNull(get);
        Assert.Equal(created.Id, get.Id);

        var list = await client.GetFromJsonAsync<List<ToolConnectionResponse>>("/api/tool-connections");
        Assert.NotNull(list);
        Assert.Contains(list, c => c.Id == created.Id);

        var update = await client.PutAsJsonAsync($"/api/tool-connections/{created.Id}", new UpdateToolConnectionRequest(
            Name: $"{name}-updated",
            Description: "Updated",
            BaseUrl: null,
            AuthType: null,
            AuthConfigJson: null,
            SecretsJson: null,
            DefaultHeadersJson: null,
            TimeoutMs: 5000,
            MaxResponseBytes: null,
            Status: "Active"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ToolConnectionResponse>();
        Assert.NotNull(updated);
        Assert.Equal($"{name}-updated", updated.Name);
        Assert.Equal(5000, updated.TimeoutMs);

        var delete = await client.DeleteAsync($"/api/tool-connections/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/tool-connections/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var name = $"dup-conn-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/tool-connections", ValidCreateRequest(name));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/tool-connections", ValidCreateRequest(name));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Delete_WithDependentTools_Returns409()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var name = $"dep-conn-{Guid.NewGuid():N}";

        var createConn = await client.PostAsJsonAsync("/api/tool-connections", ValidCreateRequest(name));
        createConn.EnsureSuccessStatusCode();
        var conn = await createConn.Content.ReadFromJsonAsync<ToolConnectionResponse>();
        Assert.NotNull(conn);

        var createTool = await client.PostAsJsonAsync("/api/tools", new CreateToolDefinitionRequest(
            ToolConnectionId: conn.Id,
            Name: $"tool_{Guid.NewGuid():N}",
            DisplayName: "Dependent Tool",
            Category: null,
            SupportContact: null,
            AiDescription: "A test tool.",
            AiUsageNotes: null,
            HttpMethod: "GET",
            PathTemplate: "/ping",
            QueryParamsJson: null,
            StaticHeadersJson: null,
            BodyTemplateJson: null,
            ParametersSchemaJson: """{"type":"object","properties":{}}""",
            ResponseMappingJson: null,
            ErrorMappingJson: null,
            NormalizeArgDiacritics: false,
            RequiresAuthenticatedUser: true,
            MaxCallsPerConversation: null,
            TimeoutMsOverride: null,
            DataClassification: "Internal"));
        Assert.Equal(HttpStatusCode.Created, createTool.StatusCode);

        var delete = await client.DeleteAsync($"/api/tool-connections/{conn.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Probe_UnreachableBaseUrl_ReturnsOkWithFailure()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var name = $"probe-conn-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/tool-connections", ValidCreateRequest(name));
        create.EnsureSuccessStatusCode();
        var conn = await create.Content.ReadFromJsonAsync<ToolConnectionResponse>();
        Assert.NotNull(conn);

        // No live network probe — unreachable host should report Ok=false without throwing.
        var probe = await client.PostAsync($"/api/tool-connections/{conn.Id}/probe", null);
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        var result = await probe.Content.ReadFromJsonAsync<ToolConnectionProbeResponse>();
        Assert.NotNull(result);
        Assert.False(result.Ok);
    }
}
