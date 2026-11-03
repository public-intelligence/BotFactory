using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class PermissionEndpointFilterTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public PermissionEndpointFilterTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SuperAdmin_BypassesToolReadRequirement()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "superadmin@example.com");
        var response = await client.GetAsync("/api/tool-connections");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BotAdmin_BypassesToolReadRequirement()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "admin@example.com");
        var response = await client.GetAsync("/api/tool-connections");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChatOnlyUser_ForbiddenOnToolRoutes()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "public@example.com");
        var response = await client.GetAsync("/api/tool-connections");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BotCreator_WithoutToolPermission_ForbiddenOnToolRoutes()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "botcreator@example.com");
        var response = await client.GetAsync("/api/tool-connections");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChatOnlyUser_ForbiddenOnBotPublish()
    {
        var admin = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var create = await admin.PostAsJsonAsync("/api/bots", new
        {
            botName = "Publish Permission Bot",
            urlSlug = $"pub-perm-{Guid.NewGuid():N}",
        });
        create.EnsureSuccessStatusCode();
        var bot = await create.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(bot);

        var chatOnly = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "public@example.com");
        var publish = await chatOnly.PostAsync($"/api/bots/{bot.Id}/publish", null);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
    }
}
