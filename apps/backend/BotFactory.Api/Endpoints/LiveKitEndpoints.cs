using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BotFactory.Api.Endpoints;

public static class LiveKitEndpoints
{
    public static void MapLiveKitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/livekit")
            .WithTags("LiveKit Agent")
            .ExcludeFromDescription();

        // Shared-secret gate: with public (anonymous) bots these endpoints are
        // internet-reachable and keyed only by conversation id, so the agent
        // must prove itself. When no secret is configured the gate is open
        // (dev); Program.cs logs a startup warning in that case.
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var secret = ctx.HttpContext.RequestServices
                .GetRequiredService<IOptions<AgentCallbackOptions>>().Value.SharedSecret;
            if (!string.IsNullOrEmpty(secret))
            {
                var header = ctx.HttpContext.Request.Headers[AgentCallbackOptions.HeaderName].FirstOrDefault() ?? string.Empty;
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(header),
                        Encoding.UTF8.GetBytes(secret)))
                {
                    return Results.Unauthorized();
                }
            }
            return await next(ctx);
        });

        // POST /livekit/chat/turn — SSE streaming (agent calls this)
        group.MapPost("/chat/turn", async (
            ChatTurnRequest request,
            HttpContext ctx,
            BotFactoryDbContext db,
            IChatTurnService chatTurn,
            CancellationToken cancellationToken) =>
        {
            var conv = await db.Conversations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken);

            if (conv is null)
                return Results.NotFound();

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            // Keepalive: external tool calls can hold the stream silent for
            // seconds; the agent's per-turn read timeout (httpx, 30 s) would
            // kill the turn. SSE comment lines are ignored by the agent parser
            // and don't change agent state, so the thinking sound keeps
            // playing. A semaphore serializes writes with the delta callback.
            //
            // Started as a plain async method (not Task.Run) so the first
            // Delay is scheduled immediately on this request's sync context /
            // timer queue — under parallel CI load, Task.Run could sit queued
            // long enough that a ~6s tool finished before the first keepalive
            // check ever ran.
            var writeLock = new SemaphoreSlim(1, 1);
            var lastWrite = System.Diagnostics.Stopwatch.StartNew();
            using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            async Task EmitKeepalivesAsync()
            {
                try
                {
                    while (!keepaliveCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), keepaliveCts.Token);
                        if (lastWrite.Elapsed < TimeSpan.FromSeconds(5))
                            continue;

                        await writeLock.WaitAsync(keepaliveCts.Token);
                        try
                        {
                            await ctx.Response.WriteAsync(": keepalive\n\n", keepaliveCts.Token);
                            await ctx.Response.Body.FlushAsync(keepaliveCts.Token);
                            lastWrite.Restart();
                        }
                        finally
                        {
                            writeLock.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Turn finished or cancelled — normal shutdown.
                }
            }

            var keepaliveTask = EmitKeepalivesAsync();

            ChatTurnProviderError? providerError = null;
            // Strip URLs/phones from spoken SSE only; ChatTurnService still
            // persists the full text for chat + the voice contact card.
            var spokenSanitizer = new VoiceSpokenSanitizer();
            try
            {
                async Task WriteSpokenDeltaAsync(string spoken, CancellationToken ct)
                {
                    if (string.IsNullOrEmpty(spoken))
                        return;
                    await writeLock.WaitAsync(ct);
                    try
                    {
                        await ctx.Response.WriteAsync($"data: {spoken}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                        lastWrite.Restart();
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }

                var result = await chatTurn.ExecuteAsync(
                    new ChatTurnExecuteRequest(request.ConversationId, request.Text, request.TurnId),
                    async (delta, ct) =>
                    {
                        // The chat keeps line breaks (lists, paragraphs); speech gets one
                        // line, exactly as before: a raw '\n' would also end the SSE frame.
                        var oneLine = delta.Replace('\n', ' ');
                        foreach (var spoken in spokenSanitizer.Push(oneLine))
                            await WriteSpokenDeltaAsync(spoken, ct);
                    },
                    cancellationToken);

                foreach (var spoken in spokenSanitizer.Flush())
                    await WriteSpokenDeltaAsync(spoken, cancellationToken);

                providerError = result.ProviderError;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            finally
            {
                keepaliveCts.Cancel();
                try
                {
                    await keepaliveTask;
                }
                catch
                {
                    /* best-effort */
                }
            }

            if (providerError is not null)
            {
                try
                {
                    var errJson = JsonSerializer.Serialize(new
                    {
                        errorCode = providerError.ErrorCode,
                        userMessage = providerError.UserMessage,
                        operatorMessage = providerError.OperatorMessage,
                    });
                    await ctx.Response.WriteAsync(
                        $"event: provider_error\ndata: {errJson}\n\n",
                        CancellationToken.None);
                    await ctx.Response.Body.FlushAsync(CancellationToken.None);
                }
                catch
                {
                    /* best-effort */
                }
            }

            return Results.Empty;
        })
        .WithName("ChatTurn")
        .Accepts<ChatTurnRequest>("application/json");

        // POST /livekit/telemetry/events — agent fire-and-forget ingestion.
        // TODO: gate this endpoint (shared secret header or LiveKit webhook signing).
        group.MapPost("/telemetry/events", async (
            TelemetryEventRequest request,
            BotFactoryDbContext db,
            ITenantContextSetter tenantSetter,
            ITelemetryService telemetry,
            IConversationService conversationService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Telemetry event: {EventType}", request.EventType);

            var conv = await db.Conversations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken);

            if (conv is not null)
                tenantSetter.SetTenant(conv.OrganizationId);

            DateTime? occurredAt = DateTime.TryParse(request.Timestamp, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var ts) ? ts : null;

            var emit = AgentEventMapper.Map(
                request.EventType,
                request.BotId,
                request.ConversationId,
                occurredAt,
                request.Data);

            if (emit is not null)
            {
                emit = emit with { UserId = conv?.UserId };
                await telemetry.EmitAsync(emit, cancellationToken);
            }

            if (string.Equals(request.EventType, "SessionPaused", StringComparison.Ordinal))
            {
                var pausedReason = MapPausedReason(request.Data);
                await conversationService.PauseAsync(request.ConversationId, pausedReason, cancellationToken);
            }
            else if (string.Equals(request.EventType, "Close", StringComparison.Ordinal))
            {
                if (conv is null)
                    return Results.NoContent();

                if (conv.Status == ConversationStatus.Paused)
                    return Results.NoContent();

                if (conv.Status == ConversationStatus.Active && IsIdleCloseData(request.Data))
                {
                    await conversationService.PauseAsync(
                        request.ConversationId,
                        ConversationPausedReasons.NoSpeechTimeout,
                        cancellationToken);
                    return Results.NoContent();
                }

                var bot = await db.Bots
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == conv.BotId, cancellationToken);

                if (conv.Status == ConversationStatus.Active
                    && bot is { EnableChat: true }
                    && AgentCloseClassifier.IsBenignVoiceSessionEndCloseData(request.Data))
                {
                    // Voice leg ended (e.g. user switched to text); keep the thread for chat.
                    return Results.NoContent();
                }

                var reason = AgentEventMapper.MapCloseReason(request.Data);
                await conversationService.CloseAsync(request.ConversationId, reason, cancellationToken);
            }

            return Results.NoContent();
        })
        .WithName("TelemetryEvent")
        .Accepts<TelemetryEventRequest>("application/json")
        .Produces(204);
    }

    private static string MapPausedReason(JsonElement? data)
    {
        if (data is not { } element || element.ValueKind != JsonValueKind.Object)
            return ConversationPausedReasons.NoSpeechTimeout;

        if (element.TryGetProperty("reason", out var reasonEl))
        {
            var reason = reasonEl.GetString();
            if (!string.IsNullOrWhiteSpace(reason))
                return reason;
        }

        return ConversationPausedReasons.NoSpeechTimeout;
    }

    private static bool IsIdleCloseData(JsonElement? data)
    {
        if (data is not { } element || element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty("error", out var errorEl)
            && errorEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(errorEl.GetString()))
        {
            return false;
        }

        if (!element.TryGetProperty("reason", out var reasonEl))
            return false;

        var lower = (reasonEl.GetString() ?? string.Empty).ToLowerInvariant();
        return lower.Contains("timeout", StringComparison.Ordinal)
            || lower.Contains("away", StringComparison.Ordinal)
            || lower.Contains("shutdown", StringComparison.Ordinal)
            || lower.Contains("no_speech", StringComparison.Ordinal)
            || lower.Contains("inactivity", StringComparison.Ordinal);
    }
}

public record ChatTurnRequest(int ConversationId, string Text, Guid TurnId);

public record TelemetryEventRequest(
    string EventType,
    int BotId,
    int ConversationId,
    string Timestamp,
    JsonElement? Data = null);
