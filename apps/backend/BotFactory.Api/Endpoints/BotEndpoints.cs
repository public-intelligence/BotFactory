using BotFactory.Api.Auth;
using BotFactory.Api.Helpers;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using System.Text.Json;

namespace BotFactory.Api.Endpoints;

public static class BotEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapBotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bots")
            .RequireAuthorization()
            .WithTags("Bots");

        // Admin catalog endpoints: no published-state filtering. Anyone with
        // permission to hit `/bots` sees every bot in their org — drafts and
        // published alike. The "published only" rule lives on the endpoints
        // where a bot is actually *used* (by-slug lookup and chat creation).
        group.MapGet("/", async (IBotService botService, int page = 1, int pageSize = 10) =>
        {
            var (bots, totalCount) = await botService.GetAllAsync(page, pageSize);
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            return Results.Ok(new PaginatedResponse<BotResponse>(
                bots.Select(ToResponse).ToList(),
                page, pageSize, totalCount, totalPages));
        })
        .RequirePermission(Permissions.BotRead)
        .WithName("GetBots")
        .Produces<PaginatedResponse<BotResponse>>();

        group.MapGet("/{id:int}", async (int id, IBotService botService) =>
        {
            var bot = await botService.GetByIdAsync(id);
            return bot is null ? Results.NotFound() : Results.Ok(ToResponse(bot));
        })
        .WithName("GetBot")
        .Produces<BotResponse>()
        .Produces(404);

        // GET /api/bots/by-slug/{slug} — public lookup for the chat landing page.
        // Anonymous on purpose: the chat page must render (or 404) before
        // prompting unauthenticated users to log in. Publishers (and admins)
        // can preview their drafts from the public URL for testing; everyone
        // else only sees bots in `published` state. It returns only what the page
        // renders (PublicBotResponse): the prompt and the rest of the configuration
        // used to be readable here by anyone.
        group.MapGet("/by-slug/{slug}", async (string slug, IBotService botService, ITenantContext tenant) =>
        {
            var publishedOnly = !tenant.CanViewDraftBots();
            var bot = await botService.GetBySlugAsync(slug, publishedOnly);
            return bot is null ? Results.NotFound() : Results.Ok(ToPublicResponse(bot));
        })
        .AllowAnonymous()
        .WithName("GetBotBySlug")
        .Produces<PublicBotResponse>()
        .Produces(404);

        group.MapPost("/", async (CreateBotRequest request, IBotService botService, ILogger<Bot> logger) =>
        {
            if (!BotInteractionModes.IsValid(request.EnableChat, request.EnableVoice))
                return Results.BadRequest(new { error = "At least one interaction mode (chat or voice) must be enabled." });

            logger.LogInformation("Creating bot \"{Name}\"...", request.BotName);
            var bot = new Bot
            {
                BotName = request.BotName,

                UrlSlug = request.UrlSlug,
                MasterPrompt = request.MasterPrompt,
                TopK = request.TopK,
                SilenceEndMs = request.SilenceEndMs,
                NoSpeechTimeoutMs = request.NoSpeechTimeoutMs,
                MaxUtteranceMs = request.MaxUtteranceMs,
                EnableBargeIn = request.EnableBargeIn,
                EnableTts = request.EnableTts,
                EnableTypingSound = request.EnableTypingSound,
                EnableChat = request.EnableChat,
                EnableVoice = request.EnableVoice,
                GroundedOnlyMessage = request.GroundedOnlyMessage,
                VoiceId = string.IsNullOrEmpty(request.VoiceId) ? null : request.VoiceId,
                VadActivationThreshold = request.VadActivationThreshold,
                VadMinSpeechDurationMs = request.VadMinSpeechDurationMs,
                InterruptionMinDurationMs = request.InterruptionMinDurationMs,
                InterruptionMinWords = request.InterruptionMinWords
            };

            var created = await botService.CreateAsync(bot);
            logger.LogInformation("Bot created: {Id} \"{Name}\"", created.Id, created.BotName);
            return Results.Created($"/api/bots/{created.Id}", ToResponse(created));
        })
        .RequirePermission(Permissions.BotUpdate)
        .WithName("CreateBot")
        .Produces<BotResponse>(201);

        group.MapPut("/{id:int}", async (int id, HttpRequest httpRequest, IBotService botService, ILogger<Bot> logger) =>
        {
            using var doc = await JsonDocument.ParseAsync(httpRequest.Body);
            var request = doc.RootElement.Deserialize<UpdateBotRequest>(JsonOptions)
                ?? new UpdateBotRequest();

            var existing = await botService.GetByIdAsync(id);
            if (existing is null)
                return Results.NotFound();

            if (request.AllowedEmbedOrigins is not null)
            {
                var invalid = EmbedOrigins.InvalidEntries(request.AllowedEmbedOrigins);
                if (invalid.Count > 0)
                    return Results.BadRequest(new { error = $"Invalid embed origin(s): {string.Join(", ", invalid)}. Use bare origins like https://example.gob.do." });
            }

            var themeError = ThemeColors.ValidateRequest(
                request.ThemeBackground, request.ThemePrimary, request.ThemeAccent);
            if (themeError is not null)
                return Results.BadRequest(new { error = themeError });

            existing.ApplyUpdate(request);
            BotUpdateHelper.ApplyGreetingFromJson(doc.RootElement, existing);

            if (!BotInteractionModes.IsValid(existing.EnableChat, existing.EnableVoice))
                return Results.BadRequest(new { error = "At least one interaction mode (chat or voice) must be enabled." });

            var updated = await botService.UpdateAsync(id, existing);
            logger.LogInformation("Bot updated: {Id} \"{Name}\"", updated!.Id, updated.BotName);
            return Results.Ok(ToResponse(updated));
        })
        .RequirePermission(Permissions.BotUpdate)
        .WithName("UpdateBot")
        .Accepts<UpdateBotRequest>("application/json")
        .Produces<BotResponse>()
        .Produces(404);

        group.MapDelete("/{id:int}", async (int id, IBotService botService, ILogger<Bot> logger) =>
        {
            var deleted = await botService.DeleteAsync(id);
            if (deleted)
                logger.LogInformation("Bot {Id} deleted", id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequirePermission(Permissions.BotDelete)
        .WithName("DeleteBot")
        .Produces(204)
        .Produces(404);

        // POST /api/bots/{id}/publish — mark a bot as published
        group.MapPost("/{id:int}/publish", async (int id, IBotService botService, ILogger<Bot> logger) =>
        {
            var updated = await botService.SetPublishedStateAsync(id, BotStates.Published);
            if (updated is null) return Results.NotFound();
            logger.LogInformation("Bot {Id} published", id);
            return Results.Ok(ToResponse(updated));
        })
        .RequirePermission(Permissions.BotPublish)
        .WithName("PublishBot")
        .Produces<BotResponse>()
        .Produces(404);

        // POST /api/bots/{id}/unpublish — revert a bot to draft
        group.MapPost("/{id:int}/unpublish", async (int id, IBotService botService, ILogger<Bot> logger) =>
        {
            var updated = await botService.SetPublishedStateAsync(id, BotStates.Draft);
            if (updated is null) return Results.NotFound();
            logger.LogInformation("Bot {Id} unpublished", id);
            return Results.Ok(ToResponse(updated));
        })
        .RequirePermission(Permissions.BotPublish)
        .WithName("UnpublishBot")
        .Produces<BotResponse>()
        .Produces(404);

        // POST /api/bots/{id}/chat — create conversation; optionally provision LiveKit room
        group.MapPost("/{id:int}/chat", async (
            int id,
            string? mode,
            IBotService botService,
            ITenantContext tenant,
            IAppSettingService appSettingService,
            LiveKitService liveKitService,
            IConversationGreetingService greetingService,
            BotFactoryDbContext db,
            ILogger<Bot> logger,
            CancellationToken cancellationToken) =>
        {
            var publishedOnly = !tenant.CanViewDraftBots();
            var bot = await botService.GetByIdAsync(id, publishedOnly);
            if (bot is null)
                return Results.NotFound();

            if (!BotInteractionModes.IsValid(bot.EnableChat, bot.EnableVoice))
                return Results.BadRequest(new { error = "Bot has no interaction modes enabled." });

            var useVoice = ResolveSessionMode(mode, bot);
            if (useVoice && !bot.EnableVoice)
                return Results.BadRequest(new { error = "Voice is not enabled for this bot." });
            if (!useVoice && !bot.EnableChat)
                return Results.BadRequest(new { error = "Chat is not enabled for this bot." });

            var conversation = new Conversation
            {
                BotId = bot.Id,
                UserId = tenant.UserId,
                Status = ConversationStatus.Active,
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);

            var conversationId = conversation.Id;
            await greetingService.PersistGreetingIfConfiguredAsync(bot, conversationId, cancellationToken);

            if (!useVoice)
            {
                logger.LogInformation("Text chat session created for bot {BotId}, conversation {ConversationId}", bot.Id, conversationId);
                return Results.Ok(new ChatSessionResponse(conversationId));
            }

            var elevenLabsKey = await appSettingService.GetDecryptedKeyAsync(ServiceType.Stt);
            if (string.IsNullOrEmpty(elevenLabsKey))
                return Results.BadRequest(new { error = "ElevenLabs API key not configured. Go to Settings to add it." });

            var metadata = VoiceChatRoomHelper.BuildRoomMetadata(bot, conversationId);
            var roomName = VoiceChatRoomHelper.NewRoomName(conversationId, isResume: false);
            var result = await liveKitService.CreateRoomAsync(metadata, new AgentJobMetadata(elevenLabsKey), VoiceChatRoomHelper.AgentName, roomName);

            logger.LogInformation("Chat room created for bot {BotId}: {RoomName}", bot.Id, result.RoomName);

            return Results.Ok(new ChatSessionResponse(conversationId, result.Token, result.Url, result.RoomName));
        })
        // bot:chat was previously implied by RequireAuthorization only; making
        // it explicit keeps a public-session principal (or any stripped-down
        // token) from starting chats against arbitrary org bots.
        .RequirePermission(Permissions.BotChat)
        .WithName("CreateChat")
        .Produces<ChatSessionResponse>()
        .Produces(404);
    }

    public static PublicBotResponse ToPublicResponse(Bot bot) => new(
        bot.Id, bot.BotName, bot.UrlSlug, bot.PublishedState, bot.PublicId,
        bot.GreetingMessage,
        bot.EnableChat, bot.EnableVoice, bot.EnableTts, bot.EnableTypingSound, bot.EnableBargeIn,
        bot.TopK, bot.SilenceEndMs, bot.MaxUtteranceMs,
        bot.RequiresAuthentication,
        bot.ThinkingSoundBlobKey is not null,
        BlobKey.VersionOf(bot.ThinkingSoundBlobKey),
        bot.ThemeBackground, bot.ThemePrimary, bot.ThemeAccent,
        bot.LogoBlobKey is not null,
        BlobKey.VersionOf(bot.LogoBlobKey));

    public static BotResponse ToResponse(Bot bot) => new(
        bot.Id, bot.OrganizationId, bot.BotName, bot.UrlSlug,
        bot.PublishedState, bot.MasterPrompt, bot.TopK,
        bot.SilenceEndMs, bot.NoSpeechTimeoutMs, bot.MaxUtteranceMs,
        bot.EnableBargeIn, bot.EnableTts, bot.EnableTypingSound,
        bot.EnableChat, bot.EnableVoice, bot.GroundedOnlyMessage,
        bot.SimilarityThreshold, bot.VoiceId,
        bot.VadActivationThreshold, bot.VadMinSpeechDurationMs,
        bot.InterruptionMinDurationMs, bot.InterruptionMinWords,
        bot.GreetingMessage,
        bot.ThinkingSoundBlobKey is not null,
        bot.ThinkingSoundFilename,
        BlobKey.VersionOf(bot.ThinkingSoundBlobKey),
        bot.RequiresAuthentication, bot.PublicId,
        bot.EmbedEnabled, bot.AllowedEmbedOrigins,
        bot.PublicRequestsPerMinute,
        bot.ThemeBackground, bot.ThemePrimary, bot.ThemeAccent,
        bot.LogoBlobKey is not null,
        bot.LogoFilename,
        BlobKey.VersionOf(bot.LogoBlobKey),
        bot.CreatedAt, bot.UpdatedAt,
        bot.RequireGrounding);

    internal static bool ResolveSessionMode(string? mode, Bot bot) => mode?.Trim().ToLowerInvariant() switch
    {
        "text" => false,
        "voice" => true,
        _ => bot.EnableVoice,
    };
}
