using System.Net;
using System.Net.Http.Json;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class PublicTokenScopeMiddlewareTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public PublicTokenScopeMiddlewareTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PublicToken_OnAdminBotsRoute_Returns403()
    {
        var session = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        using var client = PublicBotTestHelper.CreatePublicClient(_factory, session.AccessToken);

        var response = await client.GetAsync("/api/bots");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Public session tokens are limited", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicToken_OnToolRoutes_Returns403()
    {
        var session = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        using var client = PublicBotTestHelper.CreatePublicClient(_factory, session.AccessToken);

        var response = await client.GetAsync("/api/tool-connections");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicToken_OnPublicApi_AllowsOwnerConversationRead()
    {
        var session = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        using var client = PublicBotTestHelper.CreatePublicClient(_factory, session.AccessToken);

        var response = await client.GetAsync(
            $"/api/public/bots/{session.Bot.PublicId}/conversations/{session.ConversationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SessionCookie_OnAdminRoutes_AllowsAccess()
    {
        var admin = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var response = await admin.GetAsync("/api/bots");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
