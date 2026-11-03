using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services.Jobs;

public class CloseStaleConversationsJob(
    BotFactoryDbContext db,
    IConversationService conversationService,
    ILogger<CloseStaleConversationsJob> logger)
{
    private const int BatchSize = 100;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var activeConversations = await (
            from c in db.Conversations.AsNoTracking().IgnoreQueryFilters()
            join b in db.Bots.AsNoTracking().IgnoreQueryFilters() on c.BotId equals b.Id
            where c.Status == ConversationStatus.Active || c.Status == ConversationStatus.Paused
            select new
            {
                c.Id,
                c.CreatedAt,
                StaleAfterMs = (long)b.NoSpeechTimeoutMs * 2,
            }).ToListAsync(cancellationToken);

        if (activeConversations.Count == 0)
            return;

        var conversationIds = activeConversations.Select(c => c.Id).ToList();

        var lastMessageByConversation = await db.Messages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => conversationIds.Contains(m.ConversationId) && m.Role != MessageRole.Tool)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, LastAt = g.Max(m => m.CreatedAt) })
            .ToDictionaryAsync(x => x.ConversationId, x => x.LastAt, cancellationToken);

        var staleIds = new List<int>();

        foreach (var item in activeConversations)
        {
            var lastActivity = item.CreatedAt;
            if (lastMessageByConversation.TryGetValue(item.Id, out var lastMessageAt) && lastMessageAt > lastActivity)
                lastActivity = lastMessageAt;

            if (now - lastActivity >= TimeSpan.FromMilliseconds(item.StaleAfterMs))
                staleIds.Add(item.Id);
        }

        if (staleIds.Count == 0)
        {
            logger.LogInformation("close-stale-conversations: no stale active conversations");
            return;
        }

        var closedCount = 0;

        foreach (var batch in staleIds.Chunk(BatchSize))
        {
            foreach (var id in batch)
            {
                if (await conversationService.CloseAsync(id, ConversationClosedReasons.Timeout, cancellationToken))
                    closedCount++;
            }
        }

        logger.LogInformation(
            "close-stale-conversations: closed {ClosedCount} of {StaleCount} stale conversations",
            closedCount,
            staleIds.Count);
    }
}
