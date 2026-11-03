using System.IdentityModel.Tokens.Jwt;
using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Auth;
using BotFactory.Infrastructure.Data;

namespace BotFactory.Api.Endpoints;

public static class AuthEndpoints
{
    private const int SessionMaxAgeDays = 7;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth")
            .WithTags("Auth");

        // GET /auth/providers — returns available auth providers with their login URLs
        group.MapGet("/providers", (HttpContext httpContext, HttpRequest request, IAuthProvider authProvider, ILogger<Program> logger) =>
        {
            var redirectUri = GetClientOrigin(request).TrimEnd('/') + "/auth/callback";
            var providers = new List<AuthProviderInfo>
            {
                new("gobid", "GOB-ID", authProvider.GetLoginUrl(redirectUri))
            };

            var fakeProvider = httpContext.RequestServices.GetService<FakeAuthProvider>();
            if (fakeProvider is not null)
            {
                var backendOrigin = $"{request.Scheme}://{request.Host}";
                providers.Add(new("fake", "Fake-ID", backendOrigin + fakeProvider.GetLoginUrl(redirectUri)));
            }

            logger.LogInformation("Auth /providers: returning {Count} provider(s)", providers.Count);
            return Results.Ok(providers);
        })
        .AllowAnonymous()
        .WithName("GetAuthProviders")
        .Produces<List<AuthProviderInfo>>();

        // GET /auth/login?provider=gobid|fake — returns the login URL for the requested provider (default: gobid)
        group.MapGet("/login", (HttpContext httpContext, HttpRequest request, IAuthProvider authProvider, ILogger<Program> logger, string? provider) =>
        {
            var redirectUri = GetClientOrigin(request).TrimEnd('/') + "/auth/callback";
            var (activeProvider, providerName) = ResolveProvider(provider, authProvider, httpContext);
            if (activeProvider is null)
                return Results.NotFound();

            var loginUrl = activeProvider.GetLoginUrl(redirectUri);
            logger.LogInformation("Auth /login: provider={Provider}, url={LoginUrl}", providerName, loginUrl);
            return Results.Ok(new LoginUrlResponse(loginUrl));
        })
        .AllowAnonymous()
        .WithName("GetLoginUrl")
        .Produces<LoginUrlResponse>();

        // POST /auth/token — exchange authorization code for a server-side session
        group.MapPost("/token", async (TokenExchangeBody body, IAuthProvider authProvider, HttpContext httpContext,
            BotFactoryDbContext db, ILogger<Program> logger) =>
        {
            var redirectUri = GetClientOrigin(httpContext.Request).TrimEnd('/') + "/auth/callback";
            var (activeProvider, providerName) = ResolveProvider(body.Provider, authProvider, httpContext);
            if (activeProvider is null)
                return Results.NotFound();

            logger.LogInformation("Auth /token: exchanging code via {Provider} for orgAppId={OrgAppId}",
                providerName, body.OrganizationApplicationId);

            try
            {
                var result = await activeProvider.ExchangeCodeAsync(body.Code, redirectUri, body.OrganizationApplicationId);

                var session = CreateSession(result.AccessToken, result.RefreshToken, providerName);
                db.Sessions.Add(session);
                await db.SaveChangesAsync();

                SetSessionCookie(httpContext, session.Id);

                logger.LogInformation("Auth /token: session {SessionId} created via {Provider}, expires={ExpiresAt}",
                    session.Id, providerName, session.ExpiresAt);

                return Results.Ok(new AuthenticatedResponse(true));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auth /token: code exchange failed for provider={Provider}, orgAppId={OrgAppId}: {Message}",
                    body.Provider, body.OrganizationApplicationId, ex.Message);
                return Results.Problem($"Authentication failed: {ex.Message}", statusCode: 401);
            }
        })
        .AllowAnonymous()
        .WithName("ExchangeToken")
        .Produces<AuthenticatedResponse>();

        // POST /auth/logout — clear session from DB and cookie
        group.MapPost("/logout", async (HttpContext httpContext, BotFactoryDbContext db, ILogger<Program> logger) =>
        {
            var sessionCookie = httpContext.Request.Cookies[SessionAuthHandler.SessionCookieName];
            if (!string.IsNullOrEmpty(sessionCookie) && Guid.TryParse(sessionCookie, out var sessionId))
            {
                var session = await db.Sessions.FindAsync(sessionId);
                if (session is not null)
                {
                    db.Sessions.Remove(session);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Auth /logout: removed session {SessionId}", sessionId);
                }
            }

            httpContext.Response.Cookies.Delete(SessionAuthHandler.SessionCookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        })
        .AllowAnonymous()
        .WithName("Logout")
        .Produces(204);

        // GET /auth/hub-token — returns the session ID as plain text for use as a SignalR Bearer token.
        // Called by the frontend (same-origin via SWA proxy) before opening a cross-origin hub connection.
        group.MapGet("/hub-token", (HttpContext httpContext) =>
            Results.Text(httpContext.Request.Cookies[SessionAuthHandler.SessionCookieName] ?? string.Empty))
        .RequireAuthorization()
        .WithName("GetHubToken");

        // GET /auth/me — current user profile (requires valid session)
        group.MapGet("/me", (ITenantContext tenantContext) =>
        {
            return Results.Ok(new UserInfoResponse(
                tenantContext.UserId,
                tenantContext.FullName,
                tenantContext.OrganizationId,
                tenantContext.OrganizationName,
                tenantContext.Permissions));
        })
        .RequireAuthorization()
        .WithName("GetCurrentUser")
        .Produces<UserInfoResponse>();
    }

    internal static (IAuthProvider? Provider, string Name) ResolveProvider(string? provider, IAuthProvider defaultProvider, HttpContext httpContext)
    {
        var name = provider ?? "gobid";
        if (name != "fake")
            return (defaultProvider, name);

        var fakeProvider = httpContext.RequestServices.GetService<FakeAuthProvider>();
        return fakeProvider is not null ? (fakeProvider, "fake") : (null, "fake");
    }

    /// <summary>
    /// Resolves the client-facing origin, preferring Origin/Referer headers (set by the browser or dev proxy)
    /// over the backend's own Host header. This ensures redirect URIs point to the frontend (e.g. localhost:4200)
    /// rather than the backend (e.g. localhost:5000) during development.
    /// </summary>
    internal static string GetClientOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrEmpty(origin) && origin != "null")
            return origin.TrimEnd('/');

        var referer = request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
            return $"{refUri.Scheme}://{refUri.Authority}";

        var forwardedHost = request.Headers["X-Forwarded-Host"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedHost))
        {
            var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
            return $"{scheme}://{forwardedHost}";
        }

        return $"{request.Scheme}://{request.Host}";
    }

    internal static Session CreateSession(string accessToken, string? refreshToken, string providerName)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return new Session
        {
            Id = Guid.NewGuid(),
            AccessToken = accessToken,
            RefreshToken = refreshToken ?? string.Empty,
            AuthProvider = providerName,
            ExpiresAt = jwt.ValidTo,
            CreatedAt = DateTime.UtcNow,
        };
    }

    internal static void SetSessionCookie(HttpContext httpContext, Guid sessionId)
    {
        var isDevelopment = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();

        httpContext.Response.Cookies.Append(SessionAuthHandler.SessionCookieName, sessionId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = isDevelopment ? SameSiteMode.Lax : SameSiteMode.None,
            Path = "/",
            MaxAge = TimeSpan.FromDays(SessionMaxAgeDays),
        });
    }
}

public record AuthProviderInfo(string Name, string Label, string Url);
public record TokenExchangeBody(string Code, int OrganizationApplicationId, string? Provider = null);
