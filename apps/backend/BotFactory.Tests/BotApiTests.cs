using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class BotApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public BotApiTests(BotFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullCrudLifecycle_HappyPath()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        // Create
        var createRequest = new CreateBotRequest(
            BotName: "Test Bot",
            UrlSlug: "test-bot-crud",
            MasterPrompt: "You are a helpful assistant");

        var createResponse = await client.PostAsJsonAsync("/api/bots", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(created);
        Assert.Equal("Test Bot", created.BotName);

        // Get by ID
        var getResponse = await client.GetFromJsonAsync<BotResponse>($"/api/bots/{created.Id}");
        Assert.NotNull(getResponse);
        Assert.Equal(created.Id, getResponse.Id);
        Assert.Equal("Test Bot", getResponse.BotName);

        // Update
        var updateRequest = new UpdateBotRequest(BotName: "Updated Bot", TopK: 10);
        var updateResponse = await client.PutAsJsonAsync($"/api/bots/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Bot", updated.BotName);
        Assert.Equal(10, updated.TopK);

        // List
        var listResponse = await client.GetFromJsonAsync<PaginatedResponse<BotResponse>>("/api/bots");
        Assert.NotNull(listResponse);
        Assert.Contains(listResponse.Items, b => b.Id == created.Id && b.BotName == "Updated Bot");

        // Delete
        var deleteResponse = await client.DeleteAsync($"/api/bots/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify deleted
        var getDeletedResponse = await client.GetAsync($"/api/bots/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateBot_EmptyGreetingMessage_ClearsGreeting()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var createResponse = await client.PostAsJsonAsync("/api/bots", new CreateBotRequest(
            BotName: "Greeting Bot",
            UrlSlug: "greeting-bot-clear",
            GreetingMessage: "Hola!"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(created);
        Assert.Equal("¡Hola! Estoy para servirte.", created.GreetingMessage);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/bots/{created.Id}",
            new UpdateBotRequest(GreetingMessage: ""));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(updated);
        Assert.Null(updated.GreetingMessage);

        var reloaded = await client.GetFromJsonAsync<BotResponse>($"/api/bots/{created.Id}");
        Assert.NotNull(reloaded);
        Assert.Null(reloaded.GreetingMessage);
    }

    [Fact]
    public async Task UpdateBot_ExplicitNullGreetingMessage_ClearsGreeting()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var createResponse = await client.PostAsJsonAsync("/api/bots", new CreateBotRequest(
            BotName: "Greeting Bot Explicit Null",
            UrlSlug: "greeting-bot-explicit-null"));
        var created = await createResponse.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/bots/{created.Id}",
            new { greetingMessage = (string?)null, botName = created.BotName });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var reloaded = await client.GetFromJsonAsync<BotResponse>($"/api/bots/{created.Id}");
        Assert.NotNull(reloaded);
        Assert.Null(reloaded.GreetingMessage);
    }

    [Fact]
    public async Task UpdateBot_OmittedGreetingMessage_PreservesGreeting()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var createResponse = await client.PostAsJsonAsync("/api/bots", new CreateBotRequest(
            BotName: "Greeting Bot Omitted",
            UrlSlug: "greeting-bot-omitted"));
        var created = await createResponse.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(created);
        Assert.Equal("¡Hola! Estoy para servirte.", created.GreetingMessage);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/bots/{created.Id}",
            new { botName = "Renamed Bot" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var reloaded = await client.GetFromJsonAsync<BotResponse>($"/api/bots/{created.Id}");
        Assert.NotNull(reloaded);
        Assert.Equal("Renamed Bot", reloaded.BotName);
        Assert.Equal("¡Hola! Estoy para servirte.", reloaded.GreetingMessage);
    }

    [Fact]
    public async Task TenantIsolation_OrgCannotAccessOtherOrgBots()
    {
        // Different org IDs ensure tenant isolation
        var org1Client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 100);
        var org2Client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 200);

        // Org 1 creates a bot
        var createRequest = new CreateBotRequest(BotName: "Org1 Secret Bot", UrlSlug: "org1-secret");
        var createResponse = await org1Client.PostAsJsonAsync("/api/bots", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(created);

        // Org 2 cannot see it in list
        var org2List = await org2Client.GetFromJsonAsync<PaginatedResponse<BotResponse>>("/api/bots");
        Assert.NotNull(org2List);
        Assert.DoesNotContain(org2List.Items, b => b.Id == created.Id);

        // Org 2 cannot get it by ID
        var org2Get = await org2Client.GetAsync($"/api/bots/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, org2Get.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/bots");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
