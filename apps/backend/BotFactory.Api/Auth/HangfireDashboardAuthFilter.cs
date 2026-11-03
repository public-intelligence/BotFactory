using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using BotFactory.Core.Auth;
using BotFactory.Infrastructure.Data;
using Hangfire.Dashboard;
using Microsoft.IdentityModel.Tokens;

namespace BotFactory.Api.Auth;

public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        var sessionCookie = httpContext.Request.Cookies[SessionAuthHandler.SessionCookieName];
        if (string.IsNullOrEmpty(sessionCookie) || !Guid.TryParse(sessionCookie, out var sessionId))
            return false;

        var db = httpContext.RequestServices.GetRequiredService<BotFactoryDbContext>();
        var session = db.Sessions.Find(sessionId);
        if (session is null || session.ExpiresAt <= DateTime.UtcNow)
            return false;

        var tokenParams = httpContext.RequestServices.GetRequiredService<TokenValidationParameters>();
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(session.AccessToken, tokenParams, out _);

            var permClaim = principal.FindFirst("permissions")?.Value;
            if (string.IsNullOrEmpty(permClaim)) return false;

            var permissions = JsonSerializer.Deserialize<List<string>>(permClaim) ?? [];
            return permissions.Contains(Permissions.SuperAdmin)
                || permissions.Contains(Permissions.BotAdmin);
        }
        catch
        {
            return false;
        }
    }
}
