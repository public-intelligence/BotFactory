using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BotFactory.Core.Auth;
using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Auth;
using BotFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BotFactory.Api.Auth;

/// <summary>
/// Authenticates requests using the bf_session cookie.
/// Looks up the session in the DB, validates the stored JWT, and produces a ClaimsPrincipal.
/// Handles transparent token refresh when the access token expires.
/// </summary>
public class SessionAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "BfSession";
    public const string SessionCookieName = "bf_session";

    private readonly BotFactoryDbContext _db;
    private readonly TokenValidationParameters _tokenValidationParams;

    public SessionAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        BotFactoryDbContext db,
        TokenValidationParameters tokenValidationParams)
        : base(options, loggerFactory, encoder)
    {
        _db = db;
        _tokenValidationParams = tokenValidationParams;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Token sources, in order:
        //   1. Authorization: Bearer <guid>  — SignalR negotiate / long polling (cross-origin)
        //   2. ?access_token=<guid>          — SignalR WebSocket upgrade (browsers can't set WS headers)
        //   3. bf_session cookie             — normal HTTP from the SWA domain
        // The token is the session GUID obtained from GET /api/auth/hub-token while on the SWA domain.
        var rawToken = ExtractBearerToken()
            ?? (Request.Path.StartsWithSegments("/hubs") ? Request.Query["access_token"].FirstOrDefault() : null)
            ?? Request.Cookies[SessionCookieName];

        if (string.IsNullOrEmpty(rawToken))
            return AuthenticateResult.NoResult();

        // Anonymous public-session tokens are raw JWTs presented directly (no
        // Sessions row): validate statelessly. Session ids are GUIDs and never
        // contain '.', so the two token kinds can't be confused.
        if (rawToken.Contains('.'))
            return AuthenticatePublicToken(rawToken);

        if (!Guid.TryParse(rawToken, out var sessionId))
            return AuthenticateResult.NoResult();

        var session = await _db.Sessions.FindAsync(sessionId);
        if (session is null)
        {
            Logger.LogDebug("SessionAuth: session {SessionId} not found in DB", sessionId);
            return AuthenticateResult.Fail("Session not found");
        }

        // If access token is expired, try transparent refresh before validating
        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            Logger.LogInformation("SessionAuth: session {SessionId} expired, attempting refresh", sessionId);
            if (!await TryRefreshSession(session))
                return await RemoveSessionAndFail(session, "Session expired and refresh failed");
        }

        // Validate the stored JWT and extract claims
        try
        {
            var principal = ValidateJwt(session.AccessToken);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (SecurityTokenExpiredException)
        {
            // Token expired between our check and validation (race) — try refresh once more
            if (!await TryRefreshSession(session))
                return await RemoveSessionAndFail(session, "Token expired");

            var principal = ValidateJwt(session.AccessToken);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SessionAuth: JWT validation failed for session {SessionId}", sessionId);
            return AuthenticateResult.Fail($"Token validation failed: {ex.Message}");
        }
    }

    private string? ExtractBearerToken()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        return authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..].Trim()
            : null;
    }

    /// <summary>
    /// Validates a directly-presented JWT. Only anonymous public-session tokens
    /// (token_type=public) are accepted this way — provider JWTs never reach
    /// the browser and must keep flowing through server-side sessions.
    /// </summary>
    private AuthenticateResult AuthenticatePublicToken(string token)
    {
        try
        {
            var principal = ValidateJwt(token);
            var tokenType = principal.FindFirst(PublicTokenClaims.TokenType)?.Value;
            if (tokenType != PublicTokenClaims.PublicTokenType)
                return AuthenticateResult.Fail("Only public session tokens may be presented directly");

            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "SessionAuth: public token validation failed");
            return AuthenticateResult.Fail("Invalid public token");
        }
    }

    private ClaimsPrincipal ValidateJwt(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(token, _tokenValidationParams, out _);
    }

    private async Task<AuthenticateResult> RemoveSessionAndFail(Session session, string reason)
    {
        _db.Sessions.Remove(session);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The browser fires several requests at once, so when a session goes
            // bad they all land here carrying the same row: the first delete wins
            // and the rest find nothing to delete. That is the outcome we wanted
            // anyway — the session is gone — so it must not surface as a 500. Let
            // the caller fail authentication normally and answer 401.
            _db.Entry(session).State = EntityState.Detached;
        }
        Logger.LogWarning("SessionAuth: removing session {SessionId} — {Reason}", session.Id, reason);
        return AuthenticateResult.Fail(reason);
    }

    private async Task<bool> TryRefreshSession(Session session)
    {
        try
        {
            // Resolve the correct provider based on what issued this session
            IAuthProvider provider = session.AuthProvider == "fake"
                ? Context.RequestServices.GetRequiredService<FakeAuthProvider>()
                : Context.RequestServices.GetRequiredService<IAuthProvider>();

            var result = await provider.RefreshAsync(session.RefreshToken);

            session.AccessToken = result.AccessToken;
            if (result.RefreshToken is not null)
                session.RefreshToken = result.RefreshToken;

            // Read expiry from the new JWT
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
            session.ExpiresAt = jwt.ValidTo;

            await _db.SaveChangesAsync();
            Logger.LogInformation("SessionAuth: session {SessionId} refreshed successfully, new expiry={ExpiresAt}",
                session.Id, session.ExpiresAt);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SessionAuth: refresh failed for session {SessionId} (provider={Provider})",
                session.Id, session.AuthProvider);
            return false;
        }
    }
}
