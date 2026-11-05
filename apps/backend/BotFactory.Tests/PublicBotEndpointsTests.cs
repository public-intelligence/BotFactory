using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BotFactory.Core;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class PublicBotEndpointsTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public PublicBotEndpointsTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task BySlug_Anonymous_ReturnsWhatThePageRenders_NeverThePromptOrTheConfiguration()
    {
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory, b =>
        {
            b.MasterPrompt = "SECRET_PROMPT_RULES";
            b.GroundedOnlyMessage = "SECRET_GROUNDED";
            b.AllowedEmbedOrigins = "https://secret.example.com";
            b.PublicRequestsPerMinute = 30;
            b.GreetingMessage = "Hola, soy el bot.";
        });

        var anon = _factory.CreateClient();
        var response = await anon.GetAsync($"/api/bots/by-slug/{bot.UrlSlug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SECRET_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.example.com", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var hidden in new[]
                 {
                     "masterPrompt", "groundedOnlyMessage", "similarityThreshold", "allowedEmbedOrigins",
                     "publicRequestsPerMinute", "organizationId", "requireGrounding", "voiceId",
                 })
        {
            Assert.False(root.TryGetProperty(hidden, out _), $"{hidden} must not be public");
        }

        Assert.Equal(bot.PublicId, root.GetProperty("publicId").GetString());
        Assert.Equal("Hola, soy el bot.", root.GetProperty("greetingMessage").GetString());
        Assert.True(root.GetProperty("enableChat").GetBoolean());
        Assert.False(root.GetProperty("requiresAuthentication").GetBoolean());
    }

    [Fact]
    public async Task CreatePublicChat_ReturnsTokenAndAnonymousConversation()
    {
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory, b =>
        {
            b.EnableChat = true;
            b.EnableVoice = false;
        });

        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/public/bots/{bot.PublicId}/chat?mode=text", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<PublicChatSessionResponse>();
        Assert.NotNull(session);
        Assert.True(session.ConversationId > 0);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.Contains('.', session.AccessToken); // JWT
        Assert.Null(session.Token); // text mode: no LiveKit fields
        Assert.Null(session.Url);
        Assert.Null(session.RoomName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == session.ConversationId);
        Assert.Equal(bot.Id, conv.BotId);
        Assert.StartsWith("anon:", conv.UserId);
        Assert.Equal(ConversationAccessModes.Public, conv.AccessMode);
        Assert.Equal(ConversationSurfaces.Direct, conv.Surface);
        Assert.Equal(ConversationStatus.Active, conv.Status);
    }

    [Fact]
    public async Task CreatePublicChat_EmbeddedContext_SetsIframeSurface()
    {
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/public/bots/{bot.PublicId}/chat?mode=text",
            new { embedded = true, parentOrigin = "https://example.gob.do", sourceUrl = "https://example.gob.do/page?secret=1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<PublicChatSessionResponse>();
        Assert.NotNull(session);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == session.ConversationId);
        Assert.Equal(ConversationSurfaces.Iframe, conv.Surface);
        Assert.Equal("https://example.gob.do", conv.ParentOrigin);
        Assert.Equal("https://example.gob.do/page", conv.SourceUrl); // query stripped
    }

    [Fact]
    public async Task CreatePublicChat_PrivateOrDraft_Returns404()
    {
        var privateBot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory, b =>
        {
            b.RequiresAuthentication = true;
        });
        var draftBot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory, b =>
        {
            b.PublishedState = BotStates.Draft;
        });

        using var client = _factory.CreateClient();
        var privateResp = await client.PostAsync($"/api/public/bots/{privateBot.PublicId}/chat?mode=text", null);
        var draftResp = await client.PostAsync($"/api/public/bots/{draftBot.PublicId}/chat?mode=text", null);
        var missingResp = await client.PostAsync("/api/public/bots/doesNotExistPublicId000/chat?mode=text", null);

        Assert.Equal(HttpStatusCode.NotFound, privateResp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, draftResp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResp.StatusCode);
    }

    [Fact]
    public async Task CreatePublicChat_ModeMismatch_Returns400()
    {
        var chatOnly = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory, b =>
        {
            b.EnableChat = true;
            b.EnableVoice = false;
        });

        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/public/bots/{chatOnly.PublicId}/chat?mode=voice", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Voice is not enabled", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task FillAnonymousCapAsync(Bot bot)
    {
        const int defaultCap = 20;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
            tenant.SetTenant(DbSeeder.SuperAdminOrgId);
            for (var i = 0; i < defaultCap; i++)
            {
                db.Conversations.Add(new Conversation
                {
                    BotId = bot.Id,
                    UserId = $"anon:{Guid.NewGuid():N}",
                    Status = ConversationStatus.Active,
                    AccessMode = ConversationAccessModes.Public,
                    Surface = ConversationSurfaces.Direct,
                });
            }
            await db.SaveChangesAsync();
        }
    }

    private static async Task<string?> ErrorTextAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    [Fact]
    public async Task CreatePublicChat_AnonymousCap_Returns429_WithTheDefaultMessageInUsted()
    {
        // Avoid WithWebHostBuilder on the shared Database factory (dispose tears down SQL).
        // Fill the default per-bot anonymous active-session cap (20), then assert 429.
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory);
        await FillAnonymousCapAsync(bot);

        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/public/bots/{bot.PublicId}/chat?mode=text", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(BotErrorMessages.ConversationsLimitDefault, await ErrorTextAsync(response));
    }

    [Fact]
    public async Task CreatePublicChat_AnonymousCap_Returns429_WithTheBotsOwnMessage()
    {
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(
            _factory, b => b.RateLimitMessage = "Estimado ciudadano: hay mucha demanda. Llame al *462.");
        await FillAnonymousCapAsync(bot);

        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/public/bots/{bot.PublicId}/chat?mode=text", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("Estimado ciudadano: hay mucha demanda. Llame al *462.", await ErrorTextAsync(response));
    }

    [Fact]
    public async Task CreatePublicChat_PerMinuteLimit_Returns429_WithTheDefaultOrTheBotsOwnMessage()
    {
        // One request per minute for this bot: the second is rejected.
        var plain = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory, b => b.PublicRequestsPerMinute = 1);
        var custom = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory, b =>
        {
            b.PublicRequestsPerMinute = 1;
            b.RateLimitMessage = "Hay mucha demanda en este momento.";
        });

        using var client = _factory.CreateClient();
        foreach (var (bot, expected) in new[]
        {
            (plain, BotErrorMessages.RequestsLimitDefault),
            (custom, "Hay mucha demanda en este momento."),
        })
        {
            var first = await client.PostAsync($"/api/public/bots/{bot.PublicId}/chat?mode=text", null);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var second = await client.PostAsync($"/api/public/bots/{bot.PublicId}/chat?mode=text", null);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Equal(expected, await ErrorTextAsync(second));
        }
    }

    [Fact]
    public async Task CreatePublicChat_TextMode_StoresTheTextChannel()
    {
        var session = await PublicBotTestHelper.SeedAndStartAsync(_factory, _ => { }, "text");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == session.ConversationId);
        Assert.Equal(ConversationChannels.Text, conv.Channel);
    }

    [Fact]
    public async Task ResumePublicChat_InTextMode_MovesAVoiceConversationToTheTextChannel()
    {
        var session = await PublicBotTestHelper.SeedAndStartAsync(_factory, b => { b.EnableVoice = true; }, "text");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // A conversation that predates the column (or was last used by voice).
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == session.ConversationId);
            conv.Channel = ConversationChannels.Voice;
            await db.SaveChangesAsync();
        }

        using var client = PublicBotTestHelper.CreatePublicClient(_factory, session.AccessToken);
        var resume = await client.PostAsync(
            $"/api/public/bots/{session.Bot.PublicId}/conversations/{session.ConversationId}/resume?mode=text", null);
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == session.ConversationId);
        Assert.Equal(ConversationChannels.Text, updated.Channel);
    }

    [Fact]
    public async Task ResumeAndClose_OwnerSucceeds_WrongOwnerForbidden()
    {
        var owner = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        var other = await PublicBotTestHelper.SeedAndStartAsync(_factory, _ => { }, "text");

        // Wrong owner tries to resume owner's conversation on owner's publicId
        using var wrongClient = PublicBotTestHelper.CreatePublicClient(_factory, other.AccessToken);
        var forbidResume = await wrongClient.PostAsync(
            $"/api/public/bots/{owner.Bot.PublicId}/conversations/{owner.ConversationId}/resume?mode=text",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidResume.StatusCode);

        using var ownerClient = PublicBotTestHelper.CreatePublicClient(_factory, owner.AccessToken);
        var resume = await ownerClient.PostAsync(
            $"/api/public/bots/{owner.Bot.PublicId}/conversations/{owner.ConversationId}/resume?mode=text",
            null);
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        var resumed = await resume.Content.ReadFromJsonAsync<PublicChatSessionResponse>();
        Assert.NotNull(resumed);
        Assert.Equal(owner.ConversationId, resumed.ConversationId);
        Assert.False(string.IsNullOrWhiteSpace(resumed.AccessToken));

        // Close with refreshed token
        using var closeClient = PublicBotTestHelper.CreatePublicClient(_factory, resumed.AccessToken);
        var close = await closeClient.PostAsJsonAsync(
            $"/api/public/bots/{owner.Bot.PublicId}/conversations/{owner.ConversationId}/close",
            new CloseConversationRequest(ConversationClosedReasons.UserEnded));
        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);

        // Resume closed → 410
        var gone = await closeClient.PostAsync(
            $"/api/public/bots/{owner.Bot.PublicId}/conversations/{owner.ConversationId}/resume?mode=text",
            null);
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
    }

    [Fact]
    public async Task Close_WrongOwner_Returns403()
    {
        var owner = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        var other = await PublicBotTestHelper.SeedAndStartAsync(_factory);

        using var wrongClient = PublicBotTestHelper.CreatePublicClient(_factory, other.AccessToken);
        var response = await wrongClient.PostAsJsonAsync(
            $"/api/public/bots/{owner.Bot.PublicId}/conversations/{owner.ConversationId}/close",
            new CloseConversationRequest(ConversationClosedReasons.UserEnded));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetConversation_OwnerCanRead_WrongOwnerForbidden()
    {
        var owner = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        var other = await PublicBotTestHelper.SeedAndStartAsync(_factory);

        using var ownerClient = PublicBotTestHelper.CreatePublicClient(_factory, owner.AccessToken);
        var ok = await ownerClient.GetAsync(
            $"/api/public/bots/{owner.Bot.PublicId}/conversations/{owner.ConversationId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var wrongClient = PublicBotTestHelper.CreatePublicClient(_factory, other.AccessToken);
        var forbid = await wrongClient.GetAsync(
            $"/api/public/bots/{owner.Bot.PublicId}/conversations/{owner.ConversationId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbid.StatusCode);
    }

    [Fact]
    public async Task PublicToken_CannotListBots_Returns403()
    {
        var session = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        using var client = PublicBotTestHelper.CreatePublicClient(_factory, session.AccessToken);

        var response = await client.GetAsync("/api/bots");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Public session tokens are limited", body, StringComparison.OrdinalIgnoreCase);
    }
}
