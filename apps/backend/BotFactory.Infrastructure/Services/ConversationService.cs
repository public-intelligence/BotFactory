using System.Text.Json;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services;

public class ConversationService(
    BotFactoryDbContext db,
    ITelemetryService telemetry,
    ITenantContext tenantContext,
    ITenantContextSetter tenantSetter,
    ILogger<ConversationService> logger) : IConversationService
{
    public async Task<bool> PauseAsync(int conversationId, string pausedReason, CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null || conversation.Status != ConversationStatus.Active)
            return false;

        var now = DateTime.UtcNow;
        conversation.Status = ConversationStatus.Paused;
        conversation.PausedAt = now;
        conversation.PausedReason = pausedReason;
        conversation.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "conversation_paused conversationId={ConversationId} botId={BotId} reason={Reason}",
            conversation.Id,
            conversation.BotId,
            pausedReason);

        return true;
    }

    public async Task<bool> ResumeAsync(int conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null || conversation.Status != ConversationStatus.Paused)
            return false;

        var now = DateTime.UtcNow;
        conversation.Status = ConversationStatus.Active;
        conversation.PausedAt = null;
        conversation.PausedReason = null;
        conversation.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "conversation_resumed conversationId={ConversationId} botId={BotId}",
            conversation.Id,
            conversation.BotId);

        await telemetry.EmitAsync(new TelemetryEmit(
            "session_resumed",
            EventSource.Backend,
            BotId: conversation.BotId,
            ConversationId: conversation.Id,
            OccurredAt: now,
            Stage: "lifecycle"), cancellationToken);

        return true;
    }

    public async Task<bool> CloseAsync(int conversationId, string closedReason, CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
            return false;

        if (conversation.Status == ConversationStatus.Closed)
            return false;

        // Close is invoked from contexts that may lack a tenant (Hangfire job,
        // unauthenticated agent telemetry POST). Hydrate from the conversation
        // so OrganizationId stamping during SaveChangesAsync is correct.
        if (string.IsNullOrEmpty(tenantContext.OrganizationId))
            tenantSetter.SetTenant(conversation.OrganizationId);

        var now = DateTime.UtcNow;
        conversation.Status = ConversationStatus.Closed;
        conversation.PausedAt = null;
        conversation.PausedReason = null;
        conversation.ClosedAt = now;
        conversation.ClosedReason = closedReason;
        conversation.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        // CloseAsync is the single chokepoint for end-of-conversation: user "Cerrar",
        // agent's Close telemetry event, and the stale-conversation Hangfire job all
        // flow through here. Emit lifecycle telemetry from one place.
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { reason = closedReason }));
        await telemetry.EmitAsync(new TelemetryEmit(
            "conversation_ended",
            EventSource.Backend,
            BotId: conversation.BotId,
            ConversationId: conversation.Id,
            UserId: conversation.UserId,
            OccurredAt: now,
            Stage: "lifecycle",
            Data: data.RootElement.Clone()), cancellationToken);

        logger.LogInformation(
            "conversation_ended conversationId={ConversationId} botId={BotId} reason={Reason}",
            conversation.Id,
            conversation.BotId,
            closedReason);

        return true;
    }

    public async Task<PaginatedResponse<ConversationResponse>> GetByBotIdAsync(
        int botId,
        int page,
        int pageSize,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        var query = db.Conversations.Where(c => c.BotId == botId);

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<ConversationStatus>(status, ignoreCase: true, out var statusFilter))
        {
            query = query.Where(c => c.Status == statusFilter);
        }

        if (from is not null)
            query = query.Where(c => c.CreatedAt >= from);
        if (to is not null)
            query = query.Where(c => c.CreatedAt <= to);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ConversationResponse(
                c.Id,
                c.BotId,
                c.UserId,
                c.Status.ToString(),
                c.AccessMode,
                db.Messages.Count(m => m.ConversationId == c.Id),
                c.CreatedAt,
                c.ClosedAt))
            .ToListAsync();

        // The tokens of this page's conversations, for whoever may read telemetry.
        if (items.Count > 0 && tenantContext.Has(Permissions.TelemetryRead))
        {
            var usage = await LoadTokenUsageAsync(items.Select(i => i.Id).ToList());
            items = items
                .Select(i => usage.TryGetValue(i.Id, out var tokens) ? i with { Tokens = tokens } : i)
                .ToList();
        }

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new PaginatedResponse<ConversationResponse>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<ConversationDetailResponse?> GetByIdAsync(int botId, int conversationId, bool includeEvents = false)
    {
        var conversation = await db.Conversations
            .Where(c => c.Id == conversationId && c.BotId == botId)
            .FirstOrDefaultAsync();

        if (conversation is null)
            return null;

        var messages = await db.Messages
            .Where(m => m.ConversationId == conversation.Id && m.Role != MessageRole.Tool)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new MessageResponse(
                m.Id,
                m.Role.ToString(),
                m.Content,
                m.Provider,
                m.LatencyMs,
                m.CreatedAt))
            .ToListAsync();

        // Content events (e.g. kb_retrieved) ship to every viewer; admin
        // telemetry events ship only when includeEvents + telemetry:read.
        var hasTelemetryRead = tenantContext.Has(Permissions.TelemetryRead);
        var includeAdminEvents = includeEvents && hasTelemetryRead;
        var allowedEvents = await db.TelemetryEvents
            .Where(e => e.ConversationId == conversation.Id
                        && (TelemetryService.ContentEventNames.Contains(e.EventName)
                            || (includeAdminEvents && TelemetryService.FeedAllowlist.Contains(e.EventName))))
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new TelemetryEventResponse(
                e.Id,
                e.EventName,
                e.Source.ToString(),
                e.BotId,
                e.ConversationId,
                e.UserId,
                e.OccurredAt,
                e.Stage,
                e.LatencyMs,
                e.Provider,
                e.ErrorMessage,
                e.Summary,
                e.Data,
                e.TurnId))
            .ToListAsync();

        return new ConversationDetailResponse(
            conversation.Id,
            conversation.BotId,
            conversation.UserId,
            conversation.Status.ToString(),
            conversation.AccessMode,
            conversation.Surface,
            conversation.SourceUrl,
            conversation.ParentOrigin,
            conversation.CreatedAt,
            conversation.PausedAt,
            conversation.PausedReason,
            conversation.ClosedAt,
            conversation.ClosedReason,
            messages,
            allowedEvents,
            hasTelemetryRead
                ? (await LoadTokenUsageAsync([conversation.Id])).GetValueOrDefault(conversation.Id)
                : null);
    }

    /// <summary>
    /// The tokens each of these conversations spent: the sum of their `llm_usage` events. One query
    /// for the whole page; a conversation with no recorded usage has no entry.
    /// </summary>
    private async Task<Dictionary<int, TokenUsageResponse>> LoadTokenUsageAsync(IReadOnlyCollection<int> conversationIds)
    {
        var rows = await db.TelemetryEvents
            .Where(e => e.EventName == ConversationTokenUsage.EventName
                        && e.ConversationId != null
                        && conversationIds.Contains(e.ConversationId.Value))
            .Select(e => new { ConversationId = e.ConversationId!.Value, e.Data })
            .ToListAsync();

        var usage = new Dictionary<int, TokenUsageResponse>();
        foreach (var group in rows.GroupBy(r => r.ConversationId))
        {
            var sum = ConversationTokenUsage.Sum(group.Select(r => r.Data));
            if (sum is not null)
                usage[group.Key] = sum;
        }

        return usage;
    }
}
