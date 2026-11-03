using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class TelemetryService : ITelemetryService
{
    /// <summary>
    /// Events that surface in the live conversation feed (`/hubs/conversation`,
    /// admin-only). Noisy events (metrics, partials) stay persisted but do not
    /// fan out. Keep this list intentionally small — see plan §6.
    /// </summary>
    public static readonly HashSet<string> FeedAllowlist = new(StringComparer.Ordinal)
    {
        "conversation_started",
        "conversation_ended",
        "stt_failed",
        "tts_failed",
        "llm_error",
        "llm_started",
        "llm_first_token",
        // Tokens Gemini reported for one call (prompt, cached, output, thoughts).
        "llm_usage",
        "barge_in",
        "network_degraded",
        "network_recovered",
        "agent_error",
        "agent_state_changed",
        "session_paused",
        "session_resumed",
        "turn_started",
        "backend_completed",
        "turn_completed",
        // External tool executions (admin events group).
        "tool_call_completed",
        "tool_call_failed",
        "tool_call_blocked",
        // Content events (visible to all conversation viewers, not just admins).
        "kb_retrieved",
    };

    /// <summary>
    /// Subset of FeedAllowlist that contains user-visible content. Surfaced to
    /// the conversation feed regardless of telemetry:read.
    /// </summary>
    public static readonly HashSet<string> ContentEventNames = new(StringComparer.Ordinal)
    {
        "kb_retrieved",
    };

    private readonly BotFactoryDbContext _db;
    private readonly IConversationStreamPublisher _publisher;

    public TelemetryService(BotFactoryDbContext db, IConversationStreamPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task EmitAsync(TelemetryEmit emit, CancellationToken cancellationToken = default)
    {
        var entity = new TelemetryEvent
        {
            EventName = emit.EventName,
            Source = emit.Source,
            BotId = emit.BotId,
            ConversationId = emit.ConversationId,
            UserId = emit.UserId,
            TurnId = emit.TurnId,
            OccurredAt = (emit.OccurredAt ?? DateTime.UtcNow).ToUniversalTime(),
            Stage = emit.Stage,
            LatencyMs = emit.LatencyMs,
            Provider = emit.Provider,
            ErrorMessage = Truncate(emit.ErrorMessage, 2000),
            Summary = Truncate(emit.Summary, 500),
            Data = emit.Data is { } d ? JsonSerializer.Serialize(d) : null,
        };

        _db.TelemetryEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        if (entity.ConversationId is not null && FeedAllowlist.Contains(entity.EventName))
        {
            try
            {
                await _publisher.PublishEventAsync(entity, cancellationToken);
            }
            catch
            {
                // Streaming is best-effort; the persisted row is the source of truth.
            }
        }
    }

    public async Task<(List<TelemetryEvent> Items, int TotalCount)> GetEventsAsync(
        int? conversationId = null,
        int? botId = null,
        string[]? eventNames = null,
        EventSource? source = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;
        if (page < 1) page = 1;

        var query = _db.TelemetryEvents.AsNoTracking().AsQueryable();

        if (conversationId is not null)
            query = query.Where(e => e.ConversationId == conversationId);
        if (botId is not null)
            query = query.Where(e => e.BotId == botId);
        if (source is not null)
            query = query.Where(e => e.Source == source);
        if (eventNames is { Length: > 0 })
            query = query.Where(e => eventNames.Contains(e.EventName));
        if (from is not null)
            query = query.Where(e => e.OccurredAt >= from);
        if (to is not null)
            query = query.Where(e => e.OccurredAt <= to);

        query = query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<List<TelemetryEvent>> GetFeedEventsForConversationAsync(
        int conversationId,
        CancellationToken cancellationToken = default)
    {
        return await _db.TelemetryEvents.AsNoTracking()
            .Where(e => e.ConversationId == conversationId && FeedAllowlist.Contains(e.EventName))
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}
