using BotFactory.Api.Helpers;
using BotFactory.Core.Auth;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BotFactory.Api.Endpoints;

/// <summary>
/// Serves the embeddable chat document: GET /api/embed/{publicId} returns the
/// SPA shell with a per-bot Content-Security-Policy frame-ancestors header
/// computed from Bot.AllowedEmbedOrigins. It lives under /api because the
/// Static Web App only proxies /api/* to the backend — static SWA responses
/// can't carry per-bot headers. 'self' is always allowed so the admin embed
/// preview (same origin) works. The Angular route `api/embed/:slug` renders
/// the chat in embedded mode when the document boots.
/// </summary>
public static class EmbedEndpoints
{
    private const string ShellCacheKey = "embed-spa-shell";
    private static readonly TimeSpan ShellCacheTtl = TimeSpan.FromMinutes(5);

    public static void MapEmbedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/embed/{publicId}", async (
            string publicId,
            IBotService botService,
            IOptions<PublicAccessOptions> publicAccessOptions,
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            IWebHostEnvironment env,
            HttpContext httpContext,
            ILogger<Bot> logger) =>
        {
            var bot = await botService.GetByPublicIdAsync(publicId, publishedOnly: true);
            if (bot is null || bot.RequiresAuthentication || !bot.EmbedEnabled)
                return Results.NotFound();

            var html = await GetSpaShellAsync(
                publicAccessOptions.Value,
                httpClientFactory,
                cache,
                env,
                httpContext.Request.Host.Host,
                logger,
                httpContext.RequestAborted);
            if (html is null)
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Embed shell unavailable");

            var origins = EmbedOrigins.Parse(bot.AllowedEmbedOrigins);
            var frameAncestors = origins.Count > 0
                ? "'self' " + string.Join(' ', origins)
                : "'self'"; // empty allowlist = no external site may frame (decision #1)

            httpContext.Response.Headers.ContentSecurityPolicy = $"frame-ancestors {frameAncestors}";
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers["X-Robots-Tag"] = "noindex";

            return Results.Content(html, "text/html; charset=utf-8");
        })
        .AllowAnonymous()
        .RequireRateLimiting(PublicBotEndpoints.RateLimitPolicy)
        .ExcludeFromDescription();
    }

    /// <summary>
    /// The framed document must be the *current* SPA index (hashed bundle names
    /// change every build), so fetch it from the frontend host and cache it
    /// briefly. Docker stacks prefer the compose frontend container (no public
    /// hairpin); then SpaIndexUrl; then wwwroot for co-hosted setups.
    /// </summary>
    private static async Task<string?> GetSpaShellAsync(
        PublicAccessOptions options,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IWebHostEnvironment env,
        string? requestHost,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<string>(ShellCacheKey, out var cached) && cached is not null)
            return cached;

        var html = await EmbedSpaShellFetcher.FetchAsync(
            options.SpaIndexUrl,
            requestHost,
            httpClientFactory,
            logger,
            cancellationToken);

        if (html is null && !string.IsNullOrEmpty(env.WebRootPath))
        {
            var localIndex = Path.Combine(env.WebRootPath, "index.html");
            if (File.Exists(localIndex))
                html = await File.ReadAllTextAsync(localIndex, cancellationToken);
        }

        if (html is not null)
            cache.Set(ShellCacheKey, html, ShellCacheTtl);

        return html;
    }
}
