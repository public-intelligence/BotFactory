using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class BotInteractionModesTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public BotInteractionModesTests(BotFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateBot_RejectsWhenNoInteractionModeEnabled()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var request = new CreateBotRequest(
            BotName: "No Modes Bot",
            EnableChat: false,
            EnableVoice: false);

        var response = await client.PostAsJsonAsync("/api/bots", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateChat_TextOnly_SkipsLiveKitFields()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var bot = await client.PostAsJsonAsync("/api/bots", new CreateBotRequest(
            BotName: "Chat Only Bot",
            UrlSlug: "chat-only-test",
            EnableChat: true,
            EnableVoice: false));
        Assert.Equal(HttpStatusCode.Created, bot.StatusCode);
        var created = await bot.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(created);

        var session = await client.PostAsync($"/api/bots/{created.Id}/chat?mode=text", null);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var body = await session.Content.ReadFromJsonAsync<ChatSessionResponse>();
        Assert.NotNull(body);
        Assert.True(body.ConversationId > 0);
        Assert.Null(body.Token);
        Assert.Null(body.Url);
    }
}
