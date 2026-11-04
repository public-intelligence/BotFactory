using BotFactory.Api.Auth;
using BotFactory.Api.Helpers;
using BotFactory.Core;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Api.Endpoints;

public static class ConversationEndpoints
{
    public static void MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bots/{botId:int}/conversations")
            .RequireAuthorization()
            .WithTags("Conversations");

        group.MapGet("/", async (
            int botId,
            IConversationService conversationService,
            IBotService botService,
            int page = 1,
            int pageSize = 20,
            string? status = null,
            DateTime? from = null,
            DateTime? to = null) =>
        {
            var bot = await botService.GetByIdAsync(botId);
            if (bot is null)
                return Results.NotFound();

            var result = await conversationService.GetByBotIdAsync(botId, page, pageSize, status, from, to);
            return Results.Ok(result);
        })
        .RequirePermission(Permissions.ConversationRead)
        .WithName("GetConversations")
        .Produces<PaginatedResponse<ConversationResponse>>()
        .Produces(404);

        group.MapGet("/{id:int}", async (
            int botId,
            int id,
            IConversationService conversationService,
            IBotService botService,
            ITenantContext tenant,
            BotFactoryDbContext db,
            bool includeEvents = false) =>
        {
            var bot = await botService.GetByIdAsync(botId);
            if (bot is null)
                return Results.NotFound();

            var entity = await db.Conversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.BotId == botId);

            if (entity is null)
                return Results.NotFound();

            if (!ConversationAuthorization.CanView(entity, tenant))
                return Results.Forbid();

            var conversation = await conversationService.GetByIdAsync(botId, id, includeEvents);
            return conversation is null ? Results.NotFound() : Results.Ok(conversation);
        })
        .WithName("GetConversation")
        .Produces<ConversationDetailResponse>()
        .Produces(404);

        group.MapPost("/{id:int}/close", async (
            int botId,
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

            var bot = await botService.GetByIdAsync(botId);
            if (bot is null)
                return Results.NotFound();

            var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == id && c.BotId == botId, cancellationToken);

            if (conversation is null)
                return Results.NotFound();

            var isOwner = conversation.UserId == tenant.UserId;
            var isAdmin = tenant.Permissions.Contains(Permissions.SuperAdmin)
                || tenant.Permissions.Contains(Permissions.BotAdmin);

            if (!isOwner && !isAdmin)
                return Results.Forbid();

            await conversationService.CloseAsync(id, request.Reason, cancellationToken);
            return Results.NoContent();
        })
        .WithName("CloseConversation")
        .Produces(204)
        .Produces(400)
        .Produces(403)
        .Produces(404);

        group.MapPost("/{id:int}/resume", async (
            int botId,
            int id,
            string? mode,
            IBotService botService,
            IConversationService conversationService,
            IAppSettingService appSettingService,
            LiveKitService liveKitService,
            ITenantContext tenant,
            BotFactoryDbContext db,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var publishedOnly = !tenant.CanViewDraftBots();
            var bot = await botService.GetByIdAsync(botId, publishedOnly);
            if (bot is null)
                return Results.NotFound();

            var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == id && c.BotId == botId, cancellationToken);

            if (conversation is null)
                return Results.NotFound();

            var isOwner = conversation.UserId == tenant.UserId;
            var isAdmin = tenant.Permissions.Contains(Permissions.SuperAdmin)
                || tenant.Permissions.Contains(Permissions.BotAdmin);

            if (!isOwner && !isAdmin)
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

            if (!useVoice)
            {
                if (conversation.Status == ConversationStatus.Paused)
                {
                    var resumedText = await conversationService.ResumeAsync(id, cancellationToken);
                    if (!resumedText)
                        return Results.Conflict(new { error = "Conversation cannot be resumed." });
                }

                logger.LogInformation(
                    "Text chat resumed for bot {BotId} conversation {ConversationId}",
                    bot.Id,
                    id);
                return Results.Ok(new ChatSessionResponse(id));
            }

            var elevenLabsKey = await appSettingService.GetDecryptedKeyAsync(ServiceType.Stt);
            if (string.IsNullOrEmpty(elevenLabsKey))
                return Results.BadRequest(new { error = "ElevenLabs API key not configured. Go to Settings to add it." });

            if (conversation.Status == ConversationStatus.Paused)
            {
                var resumed = await conversationService.ResumeAsync(id, cancellationToken);
                if (!resumed)
                    return Results.Conflict(new { error = "Conversation cannot be resumed." });
            }

            var metadata = VoiceChatRoomHelper.BuildRoomMetadata(bot, id, isResume: true);
            var roomName = VoiceChatRoomHelper.NewRoomName(id, isResume: true);
            var result = await liveKitService.CreateRoomAsync(metadata, new AgentJobMetadata(elevenLabsKey), VoiceChatRoomHelper.AgentName, roomName);

            logger.LogInformation(
                "Chat room resumed for bot {BotId} conversation {ConversationId}: {RoomName}",
                bot.Id,
                id,
                result.RoomName);

            return Results.Ok(new ChatSessionResponse(id, result.Token, result.Url, result.RoomName));
        })
        .WithName("ResumeConversation")
        .Produces<ChatSessionResponse>()
        .Produces(404)
        .Produces(409)
        .Produces(410);
    }
}
