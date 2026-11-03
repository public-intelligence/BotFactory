using System.Net;
using System.Net.Http.Json;
using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class ConversationPauseTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ConversationPauseTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static async Task<(int BotId, int ConversationId)> SeedConversationAsync(
        BotFactoryDbContext db,
        ITenantContextSetter tenant,
        ConversationStatus status = ConversationStatus.Active,
        bool enableChat = false,
        bool enableVoice = true)
    {
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Pause Test Bot",
            UrlSlug = $"pause-{Guid.NewGuid():N}",
            PublishedState = BotStates.Published,
            MasterPrompt = "Test",
            EnableChat = enableChat,
            EnableVoice = enableVoice,
        };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "00000000-0000-0000-0000-000000000001",
            Status = status,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        return (bot.Id, conv.Id);
    }

    [Fact]
    public async Task SessionPausedTelemetry_SetsPausedFields()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant);

        var client = _factory.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var response = await client.PostAsJsonAsync("/livekit/telemetry/events", new
        {
            eventType = "SessionPaused",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = ConversationPausedReasons.NoSpeechTimeout },
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Paused, conv.Status);
        Assert.Equal(ConversationPausedReasons.NoSpeechTimeout, conv.PausedReason);
        Assert.NotNull(conv.PausedAt);
        Assert.Null(conv.ClosedAt);
    }

    [Fact]
    public async Task TelemetryClose_DoesNotCloseWhenAlreadyPaused()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant, ConversationStatus.Paused);

        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        conv.PausedAt = DateTime.UtcNow.AddMinutes(-1);
        conv.PausedReason = ConversationPausedReasons.NoSpeechTimeout;
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        await client.PostAsJsonAsync("/livekit/telemetry/events", new
        {
            eventType = "Close",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = "shutdown" },
        });

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Paused, updated.Status);
        Assert.Null(updated.ClosedAt);
    }

    [Fact]
    public async Task ResumeAsync_ActivatesPausedConversation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (_, convId) = await SeedConversationAsync(db, tenant, ConversationStatus.Paused);

        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var resumed = await conversationService.ResumeAsync(convId);
        Assert.True(resumed);

        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Active, conv.Status);
        Assert.Null(conv.PausedAt);
        Assert.Null(conv.PausedReason);
    }

    [Fact]
    public async Task ResumeConversation_Returns410WhenClosed()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant, ConversationStatus.Closed);

        var response = await client.PostAsync($"/api/bots/{botId}/conversations/{convId}/resume", null);
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task TelemetryClose_PausesWhenActiveWithIdleReason()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant);

        var client = _factory.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        await client.PostAsJsonAsync("/livekit/telemetry/events", new
        {
            eventType = "Close",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = "shutdown" },
        });

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Paused, updated.Status);
        Assert.Null(updated.ClosedAt);
    }

    [Fact]
    public async Task TelemetryClose_KeepsActiveWhenChatEnabledAndBenignDisconnectCode()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(
            db, tenant, enableChat: true, enableVoice: true);

        var client = _factory.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        await client.PostAsJsonAsync("/livekit/telemetry/events", new
        {
            eventType = "Close",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = "10" },
        });

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Active, updated.Status);
        Assert.Null(updated.ClosedAt);
    }

    [Fact]
    public async Task TelemetryClose_ClosesWhenVoiceOnlyAndBenignDisconnectCode()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant, enableChat: false);

        var client = _factory.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        await client.PostAsJsonAsync("/livekit/telemetry/events", new
        {
            eventType = "Close",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = "10" },
        });

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Closed, updated.Status);
        Assert.NotNull(updated.ClosedAt);
    }

    [Fact]
    public async Task TelemetryClose_ClosesWhenChatEnabledAndFatalError()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(
            db, tenant, enableChat: true, enableVoice: true);

        var client = _factory.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        await client.PostAsJsonAsync("/livekit/telemetry/events", new
        {
            eventType = "Close",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = "10", error = "tts failed" },
        });

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Closed, updated.Status);
        Assert.NotNull(updated.ClosedAt);
    }

    [Fact]
    public async Task ResumeConversation_DoesNotReturn409WhenActive()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant);

        var response = await client.PostAsync($"/api/bots/{botId}/conversations/{convId}/resume", null);
        Assert.NotEqual(HttpStatusCode.Conflict, response.StatusCode);
    }
}
