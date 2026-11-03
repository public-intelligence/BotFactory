using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;

namespace BotFactory.Api.Endpoints;

public static class BotThinkingSoundEndpoints
{
    public static void MapBotThinkingSoundEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bots/{botId:int}/thinking-sound")
            .RequireAuthorization()
            .WithTags("Bots");

        group.MapPost("/", async (
            int botId,
            IFormFile file,
            IBotService botService,
            IBotThinkingSoundService thinkingSoundService,
            ILogger<Bot> logger) =>
        {
            if (await botService.GetByIdAsync(botId) is null)
                return Results.NotFound();

            if (!file.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only .mp3 files are allowed." });

            if (file.Length > 5 * 1024 * 1024)
                return Results.BadRequest(new { error = "File size must be under 5MB." });

            await using var stream = file.OpenReadStream();
            var bot = await thinkingSoundService.UploadAsync(botId, file.FileName, stream);
            if (bot is null) return Results.NotFound();

            logger.LogInformation("Thinking sound uploaded for bot {BotId}: \"{Filename}\"", botId, file.FileName);
            return Results.Ok(BotEndpoints.ToResponse(bot));
        })
        .RequirePermission(Permissions.BotUpdate)
        .WithName("UploadBotThinkingSound")
        .Produces<BotResponse>()
        .Produces(404)
        .DisableAntiforgery();

        group.MapDelete("/", async (
            int botId,
            IBotThinkingSoundService thinkingSoundService,
            ILogger<Bot> logger) =>
        {
            var deleted = await thinkingSoundService.DeleteAsync(botId);
            if (deleted)
                logger.LogInformation("Thinking sound removed for bot {BotId}", botId);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequirePermission(Permissions.BotUpdate)
        .WithName("DeleteBotThinkingSound")
        .Produces(204)
        .Produces(404);

        group.MapGet("/download", async (int botId, IBotThinkingSoundService thinkingSoundService, HttpContext http, CancellationToken ct) =>
        {
            var opened = await thinkingSoundService.OpenReadAsync(botId, ct);
            return await BlobDownloadResult.StreamAsync(
                http, opened, BlobDownloadResult.AdminCacheControl, seekable: true, ct: ct);
        })
        .RequirePermission(Permissions.BotRead)
        .WithName("GetBotThinkingSoundDownloadUrl")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);
    }

    public static void MapBotThinkingSoundPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bots/by-slug/{slug}/thinking-sound/download", async (
            string slug,
            IBotThinkingSoundService thinkingSoundService,
            ITenantContext tenant,
            HttpContext http,
            CancellationToken ct) =>
        {
            var publishedOnly = !tenant.CanViewDraftBots();
            var opened = await thinkingSoundService.OpenReadBySlugAsync(slug, publishedOnly, ct);
            return await BlobDownloadResult.StreamAsync(
                http, opened, BlobDownloadResult.PublicCacheControl, seekable: true, ct: ct);
        })
        .AllowAnonymous()
        .WithName("GetBotThinkingSoundDownloadUrlBySlug")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);
    }
}
