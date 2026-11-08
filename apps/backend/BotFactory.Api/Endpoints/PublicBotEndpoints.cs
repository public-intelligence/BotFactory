using System.Text.Json;
using BotFactory.Api.Helpers;
using BotFactory.Core;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Auth;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BotFactory.Api.Endpoints;

/// <summary>
/// Anonymous access to published public bots (Bot.RequiresAuthentication ==
/// false), keyed by the opaque PublicId. Deliberately a separate surface from
/// /api/bots: these endpoints are the ONLY place a public-session token is
/// valid (enforced by PublicTokenScopeMiddleware), and everything a caller can
/// reach here is scoped to a single published public bot. Errors are sanitized
/// — never leak config details, and always 404 (not 403) for bots that exist
/// but aren't public, so existence isn't revealed.
/// </summary>
public static class PublicBotEndpoints
{
    public const string RateLimitPolicy = "public";

    public static void MapPublicBotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/public/bots/{publicId}")
            .WithTags("Public")
            .RequireRateLimiting(RateLimitPolicy);

        // POST /api/public/bots/{publicId}/chat — start an anonymous conversation.
        group.MapPost("/chat", async (
            string publicId,
            string? mode,
            PublicChatStartRequest? context,
            IBotService botService,
            ITenantContextSetter tenantSetter,
            PublicTokenService publicTokenService,
            IOptions<PublicAccessOptions> publicAccessOptions,
            IPublicRateLimiter rateLimiter,
            ITelemetryService telemetry,
            IAppSettingService appSettingService,
            IVoiceRoomLifecycle voiceRooms,
            IConversationGreetingService greetingService,
            BotFactoryDbContext db,
            ILogger<Bot> logger,
            CancellationToken cancellationToken) =>
        {
            var bot = await ResolvePublicBotAsync(botService, publicId);
            if (bot is null)
                return Results.NotFound();

            if (!BotInteractionModes.IsValid(bot.EnableChat, bot.EnableVoice))
                return Results.NotFound();

            var useVoice = BotEndpoints.ResolveSessionMode(mode, bot);
            if (useVoice && !bot.EnableVoice)
                return Results.BadRequest(new { error = "Voice is not enabled for this bot." });
            if (!useVoice && !bot.EnableChat)
                return Results.BadRequest(new { error = "Chat is not enabled for this bot." });

            var options = publicAccessOptions.Value;

            var limited = await CheckBotOrgLimitsAsync(bot, options, rateLimiter, tenantSetter, telemetry, logger, cancellationToken);
            if (limited is not null)
                return limited;

            var activeAnonymous = await db.Conversations
                .IgnoreQueryFilters()
                .CountAsync(c => c.BotId == bot.Id
                    && c.Status == ConversationStatus.Active
                    && c.UserId.StartsWith(PublicTokenClaims.AnonymousUserPrefix), cancellationToken);
            if (activeAnonymous >= Math.Max(1, options.MaxActiveAnonymousConversationsPerBot))
            {
                logger.LogWarning("Public chat rejected for bot {BotId}: anonymous session cap reached ({Cap})",
                    bot.Id, options.MaxActiveAnonymousConversationsPerBot);
                tenantSetter.SetTenant(bot.OrganizationId);
                await EmitRejectedAsync(telemetry, bot, "anonymous_session_cap", cancellationToken);
                return Results.Json(
                    new { error = BotErrorMessages.Resolve(bot.RateLimitMessage, BotErrorMessages.ConversationsLimitDefault) },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            // Anonymous conversations belong to the BOT's org (not the caller):
            // hydrate the tenant from the bot so the query filter, org stamping,
            // and per-org settings (ElevenLabs key) all resolve correctly.
            var anonymousUserId = PublicTokenService.NewAnonymousUserId();
            tenantSetter.SetTenant(bot.OrganizationId, anonymousUserId, "Visitante");

            var parentOrigin = context?.ParentOrigin is { } rawOrigin ? EmbedOrigins.NormalizeOrigin(rawOrigin) : null;
            var conversation = new Conversation
            {
                BotId = bot.Id,
                UserId = anonymousUserId,
                Status = ConversationStatus.Active,
                AccessMode = ConversationAccessModes.Public,
                Channel = ConversationChannels.For(useVoice),
                Surface = context?.Embedded == true || parentOrigin is not null
                    ? ConversationSurfaces.Iframe
                    : ConversationSurfaces.Direct,
                SourceUrl = SanitizeSourceUrl(context?.SourceUrl),
                ParentOrigin = parentOrigin,
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);

            var conversationId = conversation.Id;
            await greetingService.PersistGreetingIfConfiguredAsync(bot, conversationId, cancellationToken);

            var publicToken = publicTokenService.MintToken(bot, anonymousUserId);

            if (!useVoice)
            {
                logger.LogInformation("Anonymous text session created for bot {BotId}, conversation {ConversationId}",
                    bot.Id, conversationId);
                return Results.Ok(new PublicChatSessionResponse(
                    conversationId, publicToken.Token, publicToken.ExpiresAt));
            }

            var elevenLabsKey = await appSettingService.GetDecryptedKeyAsync(ServiceType.Stt);
            if (string.IsNullOrEmpty(elevenLabsKey))
            {
                logger.LogWarning("Anonymous voice session rejected for bot {BotId}: STT key not configured", bot.Id);
                await EmitRejectedAsync(telemetry, bot, "voice_unavailable", cancellationToken);
                return Results.Json(
                    new { error = "El servicio de voz no está disponible en este momento." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var metadata = VoiceChatRoomHelper.BuildRoomMetadata(bot, conversationId);
            var roomName = VoiceChatRoomHelper.NewRoomName(conversationId, isResume: false);
            var result = await voiceRooms.OpenAsync(
                conversation,
                metadata,
                new AgentJobMetadata(elevenLabsKey),
                VoiceChatRoomHelper.AgentName,
                roomName,
                tokenTtl: TimeSpan.FromMinutes(Math.Max(1, options.LiveKitTokenTtlMinutes)),
                canPublishData: false,
                cancellationToken: cancellationToken);

            logger.LogInformation("Anonymous voice session created for bot {BotId}: {RoomName}", bot.Id, result.RoomName);
            return Results.Ok(new PublicChatSessionResponse(
                conversationId, publicToken.Token, publicToken.ExpiresAt,
                result.Token, result.Url, result.RoomName));
        })
        .AllowAnonymous()
        .WithName("CreatePublicChat")
        .Produces<PublicChatSessionResponse>()
        .Produces(404);

        // POST /api/public/bots/{publicId}/conversations/{id}/resume — requires
        // the public session token minted at chat start; re-mints a fresh one.
        group.MapPost("/conversations/{id:int}/resume", async (
            string publicId,
            int id,
            string? mode,
            IBotService botService,
            IConversationService conversationService,
            PublicTokenService publicTokenService,
            IOptions<PublicAccessOptions> publicAccessOptions,
            IPublicRateLimiter rateLimiter,
            ITelemetryService telemetry,
            ITenantContextSetter tenantSetter,
            IAppSettingService appSettingService,
            IVoiceRoomLifecycle voiceRooms,
            ITenantContext tenant,
            BotFactoryDbContext db,
            ILogger<Bot> logger,
            CancellationToken cancellationToken) =>
        {
            var bot = await ResolvePublicBotAsync(botService, publicId);
            if (bot is null)
                return Results.NotFound();

            var limited = await CheckBotOrgLimitsAsync(
                bot, publicAccessOptions.Value, rateLimiter, tenantSetter, telemetry, logger, cancellationToken);
            if (limited is not null)
                return limited;

            var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == id && c.BotId == bot.Id, cancellationToken);
            if (conversation is null)
                return Results.NotFound();

            if (conversation.UserId != tenant.UserId)
                return Results.Forbid();

            if (conversation.Status == ConversationStatus.Closed)
                return Results.Problem(
                    statusCode: StatusCodes.Status410Gone,
                    title: "Conversation closed",
                    detail: "This conversation was closed and cannot be resumed.");

            var useVoice = BotEndpoints.ResolveSessionMode(mode, bot);
            if (useVoice && !bot.EnableVoice)
                return Results.BadRequest(new { error = "Voice is not enabled for this bot." });
            if (!useVoice && !bot.EnableChat)
                return Results.BadRequest(new { error = "Chat is not enabled for this bot." });

            // The person may come back in the other mode; the idle sweep follows the current one.
            var resumedChannel = ConversationChannels.For(useVoice);
            if (conversation.Channel != resumedChannel)
            {
                conversation.Channel = resumedChannel;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (conversation.Status == ConversationStatus.Paused)
            {
                var resumed = await conversationService.ResumeAsync(id, cancellationToken);
                if (!resumed)
                    return Results.Conflict(new { error = "Conversation cannot be resumed." });
            }

            var publicToken = publicTokenService.MintToken(bot, tenant.UserId);

            if (!useVoice)
            {
                // Back to text: the voice call is over, so its room goes and the agent in it leaves now.
                await voiceRooms.RetireAsync(conversation, cancellationToken);
                logger.LogInformation("Anonymous text session resumed for bot {BotId} conversation {ConversationId}", bot.Id, id);
                return Results.Ok(new PublicChatSessionResponse(id, publicToken.Token, publicToken.ExpiresAt));
            }

            var elevenLabsKey = await appSettingService.GetDecryptedKeyAsync(ServiceType.Stt);
            if (string.IsNullOrEmpty(elevenLabsKey))
                return Results.Json(
                    new { error = "El servicio de voz no está disponible en este momento." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var options = publicAccessOptions.Value;
            var metadata = VoiceChatRoomHelper.BuildRoomMetadata(bot, id, isResume: true);
            var roomName = VoiceChatRoomHelper.NewRoomName(id, isResume: true);
            var result = await voiceRooms.OpenAsync(
                conversation,
                metadata,
                new AgentJobMetadata(elevenLabsKey),
                VoiceChatRoomHelper.AgentName,
                roomName,
                tokenTtl: TimeSpan.FromMinutes(Math.Max(1, options.LiveKitTokenTtlMinutes)),
                canPublishData: false,
                cancellationToken: cancellationToken);

            logger.LogInformation("Anonymous voice session resumed for bot {BotId} conversation {ConversationId}: {RoomName}",
                bot.Id, id, result.RoomName);
            return Results.Ok(new PublicChatSessionResponse(
                id, publicToken.Token, publicToken.ExpiresAt,
                result.Token, result.Url, result.RoomName));
        })
        .RequireAuthorization()
        .WithName("ResumePublicConversation")
        .Produces<PublicChatSessionResponse>()
        .Produces(404)
        .Produces(409)
        .Produces(410);

        // POST /api/public/bots/{publicId}/conversations/{id}/close — graceful
        // teardown must keep working even after the bot is unpublished or made
        // private again, so this only checks ownership, not public visibility.
        group.MapPost("/conversations/{id:int}/close", async (
            string publicId,
            int id,
            CloseConversationRequest request,
            IBotService botService,
            IConversationService conversationService,
            ITenantContext tenant,
            BotFactoryDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!ConversationClosedReasons.IsClientReason(request.Reason))
                return Results.BadRequest(new { error = "Invalid close reason." });

            var bot = await botService.GetByPublicIdAsync(publicId);
            if (bot is null)
                return Results.NotFound();

            var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == id && c.BotId == bot.Id, cancellationToken);
            if (conversation is null)
                return Results.NotFound();

            if (conversation.UserId != tenant.UserId)
                return Results.Forbid();

            await conversationService.CloseAsync(id, request.Reason, cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("ClosePublicConversation")
        .Produces(204)
        .Produces(400)
        .Produces(403)
        .Produces(404);

        // GET /api/public/bots/{publicId}/conversations/{id} — history for the
        // text-chat view. Events are never included for anonymous callers.
        group.MapGet("/conversations/{id:int}", async (
            string publicId,
            int id,
            IBotService botService,
            IConversationService conversationService,
            ITenantContext tenant,
            BotFactoryDbContext db) =>
        {
            var bot = await botService.GetByPublicIdAsync(publicId);
            if (bot is null)
                return Results.NotFound();

            var conversation = await db.Conversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.BotId == bot.Id);
            if (conversation is null)
                return Results.NotFound();

            if (conversation.UserId != tenant.UserId)
                return Results.Forbid();

            var detail = await conversationService.GetByIdAsync(bot.Id, id, includeEvents: false);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .RequireAuthorization()
        .WithName("GetPublicConversation")
        .Produces<ConversationDetailResponse>()
        .Produces(403)
        .Produces(404);
    }

    /// <summary>
    /// Published + public, or it doesn't exist as far as anonymous callers are
    /// concerned. Flipping RequiresAuthentication back on immediately blocks
    /// new/resumed sessions ("block-new" revocation).
    /// </summary>
    private static async Task<Bot?> ResolvePublicBotAsync(IBotService botService, string publicId)
    {
        var bot = await botService.GetByPublicIdAsync(publicId, publishedOnly: true);
        return bot is null || bot.RequiresAuthentication ? null : bot;
    }

    /// <summary>
    /// Per-org then per-bot fixed-window caps on anonymous session traffic —
    /// they bound total load per institution/bot no matter how many client IPs
    /// it comes from (the ASP.NET per-IP limiter handles single-client abuse).
    /// Returns the 429 result to short-circuit with, or null to proceed.
    /// </summary>
    private static async Task<IResult?> CheckBotOrgLimitsAsync(
        Bot bot,
        PublicAccessOptions options,
        IPublicRateLimiter rateLimiter,
        ITenantContextSetter tenantSetter,
        ITelemetryService telemetry,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string? scope = null;
        if (!rateLimiter.TryAcquire($"org:{bot.OrganizationId}", options.OrgRequestsPerMinute))
            scope = "org";
        else if (!rateLimiter.TryAcquire($"bot:{bot.Id}", bot.PublicRequestsPerMinute ?? options.BotRequestsPerMinute))
            scope = "bot";

        if (scope is null)
            return null;

        logger.LogWarning("Public session rate-limited for bot {BotId} (scope={Scope})", bot.Id, scope);
        tenantSetter.SetTenant(bot.OrganizationId);
        await telemetry.EmitAsync(new TelemetryEmit(
            "rate_limit_exceeded",
            EventSource.Backend,
            BotId: bot.Id,
            Summary: $"Anonymous session request rate-limited ({scope} cap)",
            Data: JsonSerializer.SerializeToElement(new { scope })), cancellationToken);

        return Results.Json(
            new { error = BotErrorMessages.Resolve(bot.RateLimitMessage, BotErrorMessages.RequestsLimitDefault) },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static Task EmitRejectedAsync(
        ITelemetryService telemetry, Bot bot, string reason, CancellationToken cancellationToken) =>
        telemetry.EmitAsync(new TelemetryEmit(
            "public_session_rejected",
            EventSource.Backend,
            BotId: bot.Id,
            Summary: $"Anonymous session rejected: {reason}",
            Data: JsonSerializer.SerializeToElement(new { reason })), cancellationToken);

    /// <summary>
    /// Client-reported page URL, kept only as scheme://host/path — query
    /// strings and fragments are stripped so no tokens/PII end up persisted.
    /// </summary>
    private static string? SanitizeSourceUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var sanitized = uri.GetLeftPart(UriPartial.Path);
        return sanitized.Length <= 2000 ? sanitized : sanitized[..2000];
    }
}
