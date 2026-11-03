using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;

namespace BotFactory.Api.Endpoints;

public static class BotLogoEndpoints
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".svg",
    };

    private static readonly Dictionary<string, string> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
    };

    private const long MaxBytes = 2 * 1024 * 1024;

    public static void MapBotLogoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bots/{botId:int}/logo")
            .RequireAuthorization()
            .WithTags("Bots");

        group.MapPost("/", async (
            int botId,
            IFormFile file,
            IBotService botService,
            IBotLogoService logoService,
            ILogger<Bot> logger) =>
        {
            if (await botService.GetByIdAsync(botId) is null)
                return Results.NotFound();

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
                return Results.BadRequest(new { error = "Only PNG, JPEG, WebP, or SVG logos are allowed." });

            if (file.Length > MaxBytes)
                return Results.BadRequest(new { error = "Logo file size must be under 2MB." });

            var contentType = ContentTypesByExtension[ext];
            await using var stream = file.OpenReadStream();
            var bot = await logoService.UploadAsync(botId, file.FileName, contentType, stream);
            if (bot is null) return Results.NotFound();

            logger.LogInformation("Logo uploaded for bot {BotId}: \"{Filename}\"", botId, file.FileName);
            return Results.Ok(BotEndpoints.ToResponse(bot));
        })
        .RequirePermission(Permissions.BotUpdate)
        .WithName("UploadBotLogo")
        .Produces<BotResponse>()
        .Produces(404)
        .DisableAntiforgery();

        group.MapDelete("/", async (
            int botId,
            IBotLogoService logoService,
            ILogger<Bot> logger) =>
        {
            var deleted = await logoService.DeleteAsync(botId);
            if (deleted)
                logger.LogInformation("Logo removed for bot {BotId}", botId);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequirePermission(Permissions.BotUpdate)
        .WithName("DeleteBotLogo")
        .Produces(204)
        .Produces(404);

        group.MapGet("/download", async (int botId, IBotLogoService logoService, HttpContext http, CancellationToken ct) =>
        {
            var opened = await logoService.OpenReadAsync(botId, ct);
            return await BlobDownloadResult.StreamAsync(http, opened, BlobDownloadResult.AdminCacheControl);
        })
        .RequirePermission(Permissions.BotRead)
        .WithName("GetBotLogoDownloadUrl")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);
    }

    public static void MapBotLogoPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bots/by-slug/{slug}/logo/download", async (
            string slug,
            IBotLogoService logoService,
            ITenantContext tenant,
            HttpContext http,
            CancellationToken ct) =>
        {
            var publishedOnly = !tenant.CanViewDraftBots();
            var opened = await logoService.OpenReadBySlugAsync(slug, publishedOnly, ct);
            return await BlobDownloadResult.StreamAsync(http, opened, BlobDownloadResult.PublicCacheControl);
        })
        .AllowAnonymous()
        .WithName("GetBotLogoDownloadUrlBySlug")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);
    }
}
