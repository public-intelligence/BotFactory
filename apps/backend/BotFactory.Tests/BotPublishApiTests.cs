using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class BotPublishApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public BotPublishApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PublishAndUnpublish_TogglesPublishedState()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var create = await client.PostAsJsonAsync("/api/bots", new CreateBotRequest(
            BotName: "Publish Lifecycle Bot",
            UrlSlug: $"publish-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var bot = await create.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(bot);
        Assert.Equal("draft", bot.PublishedState);

        var publish = await client.PostAsync($"/api/bots/{bot.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var published = await publish.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(published);
        Assert.Equal("published", published.PublishedState);

        var unpublish = await client.PostAsync($"/api/bots/{bot.Id}/unpublish", null);
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        var draft = await unpublish.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(draft);
        Assert.Equal("draft", draft.PublishedState);
    }

    [Fact]
    public async Task Publish_MissingBot_Returns404()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var response = await client.PostAsync("/api/bots/999999/publish", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChatOnlyUser_ForbiddenOnPublish()
    {
        var admin = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var create = await admin.PostAsJsonAsync("/api/bots", new CreateBotRequest(
            BotName: "Forbidden Publish Bot",
            UrlSlug: $"forbid-pub-{Guid.NewGuid():N}"));
        create.EnsureSuccessStatusCode();
        var bot = await create.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(bot);

        var chatOnly = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "public@example.com");
        var publish = await chatOnly.PostAsync($"/api/bots/{bot.Id}/publish", null);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
    }
}
