using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;

namespace BotFactory.Api.Endpoints;

public static class OrgBrandingEndpoints
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> LogoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".webp", ".jpg", ".jpeg",
    };
    private static readonly HashSet<string> FaviconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".webp", ".jpg", ".jpeg", ".ico",
    };

    public static void MapOrgBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings/branding")
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("/", async (IOrgBrandingService service) =>
        {
            var branding = await service.GetAsync();
            return Results.Ok(ToResponse(branding));
        })
        .RequirePermission(Permissions.SettingsRead)
        .WithName("GetOrgBranding")
        .Produces<OrgBrandingResponse>();

        group.MapPost("/logo", async (
            IFormFile file,
            IOrgBrandingService service,
            ILogger<OrgBranding> logger) =>
        {
            var validation = ValidateUpload(file, LogoExtensions, "logo");
            if (validation is not null) return validation;

            await using var stream = file.OpenReadStream();
            var branding = await service.UploadLogoAsync(
                file.FileName, stream, ContentTypeFor(file.FileName));
            logger.LogInformation("Org logo uploaded: \"{Filename}\"", file.FileName);
            return Results.Ok(ToResponse(branding));
        })
        .RequirePermission(Permissions.SettingsUpdate)
        .WithName("UploadOrgLogo")
        .Produces<OrgBrandingResponse>()
        .DisableAntiforgery();

        group.MapDelete("/logo", async (IOrgBrandingService service, ILogger<OrgBranding> logger) =>
        {
            var deleted = await service.DeleteLogoAsync();
            if (deleted)
                logger.LogInformation("Org logo removed");
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequirePermission(Permissions.SettingsUpdate)
        .WithName("DeleteOrgLogo")
        .Produces(204)
        .Produces(404);

        group.MapGet("/logo/download", async (IOrgBrandingService service, HttpContext http, CancellationToken ct) =>
        {
            var opened = await service.OpenLogoReadAsync(ct);
            return await BlobDownloadResult.StreamAsync(http, opened, BlobDownloadResult.AdminCacheControl);
        })
        .RequirePermission(Permissions.SettingsRead)
        .WithName("GetOrgLogoDownloadUrl")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);

        group.MapPost("/favicon", async (
            IFormFile file,
            IOrgBrandingService service,
            ILogger<OrgBranding> logger) =>
        {
            var validation = ValidateUpload(file, FaviconExtensions, "favicon");
            if (validation is not null) return validation;

            await using var stream = file.OpenReadStream();
            var branding = await service.UploadFaviconAsync(
                file.FileName, stream, ContentTypeFor(file.FileName));
            logger.LogInformation("Org favicon uploaded: \"{Filename}\"", file.FileName);
            return Results.Ok(ToResponse(branding));
        })
        .RequirePermission(Permissions.SettingsUpdate)
        .WithName("UploadOrgFavicon")
        .Produces<OrgBrandingResponse>()
        .DisableAntiforgery();

        group.MapDelete("/favicon", async (IOrgBrandingService service, ILogger<OrgBranding> logger) =>
        {
            var deleted = await service.DeleteFaviconAsync();
            if (deleted)
                logger.LogInformation("Org favicon removed");
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequirePermission(Permissions.SettingsUpdate)
        .WithName("DeleteOrgFavicon")
        .Produces(204)
        .Produces(404);

        group.MapGet("/favicon/download", async (IOrgBrandingService service, HttpContext http, CancellationToken ct) =>
        {
            var opened = await service.OpenFaviconReadAsync(ct);
            return await BlobDownloadResult.StreamAsync(http, opened, BlobDownloadResult.AdminCacheControl);
        })
        .RequirePermission(Permissions.SettingsRead)
        .WithName("GetOrgFaviconDownloadUrl")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);
    }

    public static void MapOrgBrandingPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bots/by-slug/{slug}/branding/favicon/download", async (
            string slug,
            IOrgBrandingService brandingService,
            ITenantContext tenant,
            HttpContext http,
            CancellationToken ct) =>
        {
            var publishedOnly = !tenant.CanViewDraftBots();
            var opened = await brandingService.OpenFaviconReadBySlugAsync(slug, publishedOnly, ct);
            return await BlobDownloadResult.StreamAsync(http, opened, BlobDownloadResult.PublicCacheControl);
        })
        .AllowAnonymous()
        .WithName("GetOrgFaviconDownloadUrlBySlug")
        .WithTags("Settings")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);
    }

    private static IResult? ValidateUpload(IFormFile file, HashSet<string> allowedExtensions, string label)
    {
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !allowedExtensions.Contains(ext))
        {
            var allowed = string.Join(", ", allowedExtensions.OrderBy(e => e));
            return Results.BadRequest(new { error = $"Only {allowed} files are allowed for {label}." });
        }

        if (file.Length > MaxFileBytes)
            return Results.BadRequest(new { error = "File size must be under 2MB." });

        return null;
    }

    private static string ContentTypeFor(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };
    }

    private static OrgBrandingResponse ToResponse(OrgBranding? b) => new(
        b?.LogoFilename,
        b?.LogoBlobKey is not null,
        BlobKey.VersionOf(b?.LogoBlobKey),
        b?.FaviconFilename,
        b?.FaviconBlobKey is not null,
        BlobKey.VersionOf(b?.FaviconBlobKey));
}
