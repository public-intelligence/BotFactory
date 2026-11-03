using BotFactory.Core.Auth;

namespace BotFactory.Api.Middleware;

/// <summary>
/// Confines anonymous public-session principals to the public surface. A valid
/// public JWT authenticates like any principal, so without this guard it could
/// reach authenticated endpoints that only check RequireAuthorization (e.g. the
/// /api/bots group). Public tokens are only meaningful on /api/public/** and
/// the SignalR hub; anywhere else is rejected outright.
/// </summary>
public class PublicTokenScopeMiddleware
{
    private static readonly PathString PublicApiPrefix = new("/api/public");
    private static readonly PathString HubsPrefix = new("/hubs");

    private readonly RequestDelegate _next;

    public PublicTokenScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && user.FindFirst(PublicTokenClaims.TokenType)?.Value == PublicTokenClaims.PublicTokenType
            && !context.Request.Path.StartsWithSegments(PublicApiPrefix)
            && !context.Request.Path.StartsWithSegments(HubsPrefix))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Public session tokens are limited to public endpoints." });
            return;
        }

        await _next(context);
    }
}
