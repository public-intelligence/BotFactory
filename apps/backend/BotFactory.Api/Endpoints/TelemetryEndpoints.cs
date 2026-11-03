using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/telemetry")
            .RequireAuthorization()
            .WithTags("Telemetry");

        group.MapGet("/events", async (
            ITelemetryService telemetry,
            int? conversationId = null,
            int? botId = null,
            string? eventNames = null,
            string? source = null,
            DateTime? from = null,
            DateTime? to = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default) =>
        {
            EventSource? src = source is null ? null : Enum.TryParse<EventSource>(source, true, out var s) ? s : null;
            var names = string.IsNullOrWhiteSpace(eventNames)
                ? null
                : eventNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var (items, totalCount) = await telemetry.GetEventsAsync(
                conversationId, botId, names, src, from, to, page, pageSize, cancellationToken);

            var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);
            return Results.Ok(new PaginatedResponse<TelemetryEventResponse>(
                items.Select(ToResponse).ToList(),
                page, pageSize, totalCount, totalPages));
        })
        .RequirePermission(Permissions.TelemetryRead)
        .WithName("GetTelemetryEvents")
        .Produces<PaginatedResponse<TelemetryEventResponse>>();

        // GET /api/telemetry/public-usage — aggregates for the dashboard's
        // public-vs-authenticated panel. Org-scoped via the tenant query filter
        // (anonymous conversations belong to the bot's org).
        group.MapGet("/public-usage", async (
            BotFactoryDbContext db,
            DateTime? from = null,
            DateTime? to = null,
            CancellationToken cancellationToken = default) =>
        {
            var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
            var fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();
            if (fromUtc > toUtc) (fromUtc, toUtc) = (toUtc, fromUtc);

            var conversations = db.Conversations.AsNoTracking()
                .Where(c => c.CreatedAt >= fromUtc && c.CreatedAt <= toUtc);

            var byMode = await conversations
                .GroupBy(c => c.AccessMode)
                .Select(g => new { Mode = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var bySurface = await conversations
                .Where(c => c.AccessMode == ConversationAccessModes.Public)
                .GroupBy(c => c.Surface)
                .Select(g => new { Surface = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            // Anonymous projection first: EF can't translate ordering on a
            // constructor-projected record member.
            var topOriginsRaw = await conversations
                .Where(c => c.AccessMode == ConversationAccessModes.Public && c.ParentOrigin != null)
                .GroupBy(c => c.ParentOrigin!)
                .Select(g => new { Origin = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(cancellationToken);
            var topOrigins = topOriginsRaw
                .Select(x => new PublicUsageOriginCount(x.Origin, x.Count))
                .ToList();

            string[] failureEvents = ["public_session_rejected", "rate_limit_exceeded"];
            var failureCounts = await db.TelemetryEvents.AsNoTracking()
                .Where(e => e.OccurredAt >= fromUtc && e.OccurredAt <= toUtc
                    && failureEvents.Contains(e.EventName))
                .GroupBy(e => new { e.OccurredAt.Date, e.EventName })
                .Select(g => new { g.Key.Date, g.Key.EventName, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var dailyConversations = await conversations
                .GroupBy(c => new { c.CreatedAt.Date, c.AccessMode })
                .Select(g => new { g.Key.Date, g.Key.AccessMode, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var daily = dailyConversations.Select(d => d.Date)
                .Concat(failureCounts.Select(f => f.Date))
                .Distinct()
                .OrderBy(d => d)
                .Select(date => new PublicUsageDaily(
                    date,
                    dailyConversations.Where(d => d.Date == date && d.AccessMode == ConversationAccessModes.Public).Sum(d => d.Count),
                    dailyConversations.Where(d => d.Date == date && d.AccessMode == ConversationAccessModes.Authenticated).Sum(d => d.Count),
                    failureCounts.Where(f => f.Date == date && f.EventName == "public_session_rejected").Sum(f => f.Count),
                    failureCounts.Where(f => f.Date == date && f.EventName == "rate_limit_exceeded").Sum(f => f.Count)))
                .ToList();

            return Results.Ok(new PublicUsageResponse(
                fromUtc,
                toUtc,
                byMode.Where(m => m.Mode == ConversationAccessModes.Authenticated).Sum(m => m.Count),
                byMode.Where(m => m.Mode == ConversationAccessModes.Public).Sum(m => m.Count),
                bySurface.Where(s => s.Surface == ConversationSurfaces.Direct).Sum(s => s.Count),
                bySurface.Where(s => s.Surface == ConversationSurfaces.Iframe).Sum(s => s.Count),
                topOrigins,
                failureCounts.Where(f => f.EventName == "public_session_rejected").Sum(f => f.Count),
                failureCounts.Where(f => f.EventName == "rate_limit_exceeded").Sum(f => f.Count),
                daily));
        })
        .RequirePermission(Permissions.TelemetryRead)
        .WithName("GetPublicUsage")
        .Produces<PublicUsageResponse>();
    }

    private static TelemetryEventResponse ToResponse(TelemetryEvent e) => new(
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
        e.TurnId);
}
