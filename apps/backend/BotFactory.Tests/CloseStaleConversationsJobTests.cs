using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services.Jobs;
using BotFactory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class CloseStaleConversationsJobTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public CloseStaleConversationsJobTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ExecuteAsync_ClosesConversationInactiveLongerThanTwiceNoSpeechTimeout()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Stale Job Bot",
            UrlSlug = $"stale-{Guid.NewGuid():N}",
            PublishedState = BotStates.Published,
            MasterPrompt = "Test",
            NoSpeechTimeoutMs = 15_000,
        };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "stale-tester",
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var staleSince = DateTime.UtcNow.AddSeconds(-60);
        conv.CreatedAt = staleSince;
        conv.UpdatedAt = staleSince;
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<CloseStaleConversationsJob>();
        await job.ExecuteAsync();

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conv.Id);
        Assert.Equal(ConversationStatus.Closed, updated.Status);
        Assert.Equal(ConversationClosedReasons.Timeout, updated.ClosedReason);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsActiveWhenRecentMessageWithinWindow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Stale Job Bot Recent",
            UrlSlug = $"stale-recent-{Guid.NewGuid():N}",
            PublishedState = BotStates.Published,
            MasterPrompt = "Test",
            NoSpeechTimeoutMs = 15_000,
        };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "stale-tester",
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var oldStart = DateTime.UtcNow.AddMinutes(-10);
        conv.CreatedAt = oldStart;
        conv.UpdatedAt = oldStart;
        await db.SaveChangesAsync();

        db.Messages.Add(new Message
        {
            ConversationId = conv.Id,
            Role = MessageRole.User,
            Content = "Hola",
            CreatedAt = DateTime.UtcNow.AddSeconds(-5),
            UpdatedAt = DateTime.UtcNow.AddSeconds(-5),
        });
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<CloseStaleConversationsJob>();
        await job.ExecuteAsync();

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conv.Id);
        Assert.Equal(ConversationStatus.Active, updated.Status);
    }

    [Fact]
    public async Task ExecuteAsync_UsesLargerThresholdForBotWithLongerNoSpeechTimeout()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Stale Job Bot Long Timeout",
            UrlSlug = $"stale-long-{Guid.NewGuid():N}",
            PublishedState = BotStates.Published,
            MasterPrompt = "Test",
            NoSpeechTimeoutMs = 60_000,
        };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "stale-tester",
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var staleSince = DateTime.UtcNow.AddSeconds(-90);
        conv.CreatedAt = staleSince;
        conv.UpdatedAt = staleSince;
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<CloseStaleConversationsJob>();
        await job.ExecuteAsync();

        await using var readScope1 = _factory.Services.CreateAsyncScope();
        var readDb1 = readScope1.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated1 = await readDb1.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conv.Id);
        Assert.Equal(ConversationStatus.Active, updated1.Status);

        var older = DateTime.UtcNow.AddSeconds(-130);
        conv.CreatedAt = older;
        conv.UpdatedAt = older;
        await db.SaveChangesAsync();

        await job.ExecuteAsync();

        await using var readScope2 = _factory.Services.CreateAsyncScope();
        var readDb2 = readScope2.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated2 = await readDb2.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conv.Id);
        Assert.Equal(ConversationStatus.Closed, updated2.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ClosesPausedConversationInactiveLongerThanTwiceNoSpeechTimeout()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Stale Paused Bot",
            UrlSlug = $"stale-paused-{Guid.NewGuid():N}",
            PublishedState = BotStates.Published,
            MasterPrompt = "Test",
            NoSpeechTimeoutMs = 15_000,
        };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "stale-tester",
            Status = ConversationStatus.Paused,
            PausedAt = DateTime.UtcNow.AddMinutes(-5),
            PausedReason = ConversationPausedReasons.NoSpeechTimeout,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var staleSince = DateTime.UtcNow.AddSeconds(-60);
        conv.CreatedAt = staleSince;
        conv.UpdatedAt = staleSince;
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<CloseStaleConversationsJob>();
        await job.ExecuteAsync();

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var updated = await readDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conv.Id);
        Assert.Equal(ConversationStatus.Closed, updated.Status);
        Assert.Equal(ConversationClosedReasons.Timeout, updated.ClosedReason);
    }

    // ---- Text conversations: a person reads and types, so they get minutes, not 30 seconds. ----

    [Fact]
    public async Task ExecuteAsync_KeepsTextConversationQuietLongerThanTheVoiceWindow()
    {
        // The case seen under load on 2026-11-04: a visitor who takes more than 30 s between
        // messages found the conversation closed when the sweep ran.
        var id = await SeedConversationAsync(ConversationChannels.Text, quietFor: TimeSpan.FromMinutes(5));

        await RunJobAsync();

        Assert.Equal(ConversationStatus.Active, (await ReadAsync(id)).Status);
    }

    [Fact]
    public async Task ExecuteAsync_ClosesTextConversationQuietLongerThanTheTextWindow()
    {
        var id = await SeedConversationAsync(ConversationChannels.Text, quietFor: TimeSpan.FromMinutes(16));

        await RunJobAsync();

        var updated = await ReadAsync(id);
        Assert.Equal(ConversationStatus.Closed, updated.Status);
        Assert.Equal(ConversationClosedReasons.Timeout, updated.ClosedReason);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsTextConversationWithRecentMessage()
    {
        var id = await SeedConversationAsync(
            ConversationChannels.Text, quietFor: TimeSpan.FromMinutes(40), lastMessageAgo: TimeSpan.FromMinutes(3));

        await RunJobAsync();

        Assert.Equal(ConversationStatus.Active, (await ReadAsync(id)).Status);
    }

    [Fact]
    public async Task ExecuteAsync_StillClosesVoiceConversationAfterTwiceTheNoSpeechTimeout()
    {
        var id = await SeedConversationAsync(ConversationChannels.Voice, quietFor: TimeSpan.FromSeconds(60));

        await RunJobAsync();

        Assert.Equal(ConversationStatus.Closed, (await ReadAsync(id)).Status);
    }

    private async Task<int> SeedConversationAsync(string channel, TimeSpan quietFor, TimeSpan? lastMessageAgo = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Stale Job Channel Bot",
            UrlSlug = $"stale-ch-{Guid.NewGuid():N}",
            PublishedState = BotStates.Published,
            MasterPrompt = "Test",
            NoSpeechTimeoutMs = 15_000,
        };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "stale-tester",
            Status = ConversationStatus.Active,
            Channel = channel,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var since = DateTime.UtcNow - quietFor;
        conv.CreatedAt = since;
        conv.UpdatedAt = since;
        await db.SaveChangesAsync();

        if (lastMessageAgo is { } ago)
        {
            var at = DateTime.UtcNow - ago;
            db.Messages.Add(new Message
            {
                ConversationId = conv.Id,
                Role = MessageRole.User,
                Content = "Hola",
                CreatedAt = at,
                UpdatedAt = at,
            });
            await db.SaveChangesAsync();
        }

        return conv.Id;
    }

    private async Task RunJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<CloseStaleConversationsJob>();
        await job.ExecuteAsync();
    }

    private async Task<Conversation> ReadAsync(int id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        return await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == id);
    }
}
