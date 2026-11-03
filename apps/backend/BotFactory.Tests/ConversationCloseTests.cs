using System.Net;
using System.Net.Http.Json;
using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class ConversationCloseTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ConversationCloseTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static async Task<(int BotId, int ConversationId)> SeedConversationAsync(
        BotFactoryDbContext db,
        ITenantContextSetter tenant,
        string userId = "00000000-0000-0000-0000-000000000001",
        Action<Bot>? configureBot = null)
    {
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Close Test Bot",
            UrlSlug = $"close-{Guid.NewGuid():N}",
            PublishedState = BotStates.Published,
            MasterPrompt = "Test",
        };
        configureBot?.Invoke(bot);
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = userId,
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        return (bot.Id, conv.Id);
    }

    [Fact]
    public async Task CloseConversation_SetsClosedFields()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant);

        var response = await client.PostAsJsonAsync(
            $"/api/bots/{botId}/conversations/{convId}/close",
            new { reason = ConversationClosedReasons.UserEnded });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Closed, conv.Status);
        Assert.NotNull(conv.ClosedAt);
        Assert.Equal(ConversationClosedReasons.UserEnded, conv.ClosedReason);
    }

    [Fact]
    public async Task CloseConversation_IsIdempotent()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant);

        await client.PostAsJsonAsync(
            $"/api/bots/{botId}/conversations/{convId}/close",
            new { reason = ConversationClosedReasons.UserEnded });

        await using var readScope1 = _factory.Services.CreateAsyncScope();
        var readDb1 = readScope1.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv1 = await readDb1.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        var firstClosedAt = conv1.ClosedAt;

        var response = await client.PostAsJsonAsync(
            $"/api/bots/{botId}/conversations/{convId}/close",
            new { reason = ConversationClosedReasons.UserAborted });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var readScope2 = _factory.Services.CreateAsyncScope();
        var readDb2 = readScope2.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv2 = await readDb2.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(firstClosedAt, conv2.ClosedAt);
        Assert.Equal(ConversationClosedReasons.UserEnded, conv2.ClosedReason);
    }

    [Fact]
    public async Task CloseConversation_ForbidWhenNotOwner()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(
            _factory,
            email: "public@example.com");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(
            db,
            tenant,
            userId: "00000000-0000-0000-0000-000000000001");

        var response = await client.PostAsJsonAsync(
            $"/api/bots/{botId}/conversations/{convId}/close",
            new { reason = ConversationClosedReasons.UserEnded });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TelemetryClose_ClosesActiveConversation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant);

        var client = _factory.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        // A Close with an error is terminal. An error-free idle reason
        // ("shutdown"/"timeout"/…) now PAUSES an active conversation instead —
        // that path is covered by ConversationPauseTests.
        var payload = new
        {
            eventType = "Close",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = "shutdown", error = "fatal: stt stream failed" },
        };

        var response = await client.PostAsJsonAsync("/livekit/telemetry/events", payload);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Closed, conv.Status);
        Assert.Equal(ConversationClosedReasons.Timeout, conv.ClosedReason);
    }

    [Fact]
    public async Task TelemetryClose_DoesNotOverwriteUserEnded()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var (botId, convId) = await SeedConversationAsync(db, tenant);

        await client.PostAsJsonAsync(
            $"/api/bots/{botId}/conversations/{convId}/close",
            new { reason = ConversationClosedReasons.UserEnded });

        var lkClient = _factory.CreateClient();
        lkClient.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        await lkClient.PostAsJsonAsync("/livekit/telemetry/events", new
        {
            eventType = "Close",
            botId,
            conversationId = convId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new { reason = "shutdown" },
        });

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var conv = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationClosedReasons.UserEnded, conv.ClosedReason);
    }
}
