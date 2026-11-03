using System.Text;
using BotFactory.Api.Auth;
using BotFactory.Infrastructure.Auth;
using BotFactory.Infrastructure.Data;

namespace BotFactory.Api.Endpoints;

public static class DevAuthEndpoints
{
    public static void MapDevAuthEndpoints(this IEndpointRouteBuilder app)
    {
        if (app.ServiceProvider.GetService<FakeAuthProvider>() is null) return;

        app.MapGet("/auth/dev/login", (string redirectUri) =>
        {
            var users = FakeAuthProvider.GetSeededUserSummaries();
            var html = BuildLoginPage(users, redirectUri);
            return Results.Content(html, "text/html");
        })
        .AllowAnonymous()
        .WithTags("DevAuth")
        .WithName("DevLogin")
        .ExcludeFromDescription();

        // POST /auth/dev/token — create a session directly from a fake user email (for tests and dev tools)
        app.MapPost("/auth/dev/token", async (DevTokenRequest body,
            BotFactoryDbContext db, HttpContext httpContext, ILogger<Program> logger) =>
        {
            var fakeProvider = httpContext.RequestServices.GetService<FakeAuthProvider>();
            if (fakeProvider is null)
                return Results.NotFound("FakeAuthProvider is not registered");

            var email = body.Email ?? "superadmin@example.com";
            var orgAppId = body.OrganizationApplicationId ?? 1;

            logger.LogInformation("Auth /dev/token: creating session for {Email}, orgAppId={OrgAppId}", email, orgAppId);

            var result = await fakeProvider.ExchangeCodeAsync(email, "dev-redirect", orgAppId);

            var session = AuthEndpoints.CreateSession(result.AccessToken, result.RefreshToken, "fake");
            db.Sessions.Add(session);
            await db.SaveChangesAsync();

            AuthEndpoints.SetSessionCookie(httpContext, session.Id);

            logger.LogInformation("Auth /dev/token: session {SessionId} created for {Email}", session.Id, email);

            return Results.Ok(new { authenticated = true, sessionId = session.Id });
        })
        .AllowAnonymous()
        .WithTags("DevAuth")
        .WithName("DevExchangeToken")
        .ExcludeFromDescription();
    }

    private static string BuildLoginPage(
        IReadOnlyList<(string Email, string FullName, string Role)> users,
        string redirectUri)
    {
        var cards = new StringBuilder();
        foreach (var (email, fullName, role) in users)
        {
            cards.Append($"""
                <a href="#" class="card" data-email="{email}">
                    <div class="name">{fullName}</div>
                    <div class="role">{role}</div>
                    <div class="email">{email}</div>
                </a>
            """);
        }

        var escapedRedirectUri = redirectUri.Replace("'", "\\'");

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Fake-ID Login</title>
                <style>
                    * { margin: 0; padding: 0; box-sizing: border-box; }
                    body { font-family: system-ui, sans-serif; background: #f1f5f9; display: flex; justify-content: center; padding: 2rem; }
                    .container { max-width: 600px; width: 100%; }
                    h1 { text-align: center; margin-bottom: 0.5rem; color: #1e293b; }
                    .subtitle { text-align: center; color: #64748b; margin-bottom: 2rem; }
                    .grid { display: grid; gap: 0.75rem; }
                    .card { display: block; background: white; border-radius: 0.5rem; padding: 1rem 1.25rem; text-decoration: none; color: inherit; border: 1px solid #e2e8f0; transition: box-shadow 0.15s; }
                    .card:hover { box-shadow: 0 2px 8px rgba(0,0,0,0.1); border-color: #3b82f6; }
                    .name { font-weight: 600; color: #1e293b; }
                    .role { font-size: 0.875rem; color: #3b82f6; margin-top: 0.125rem; }
                    .email { font-size: 0.8rem; color: #94a3b8; margin-top: 0.25rem; }
                    .org-field { display: flex; align-items: center; gap: 0.5rem; justify-content: center; margin-bottom: 1.5rem; padding: 0.75rem; background: white; border-radius: 0.5rem; border: 1px solid #e2e8f0; }
                    .org-field label { font-size: 0.875rem; color: #475569; font-weight: 500; white-space: nowrap; }
                    .org-field input { width: 80px; padding: 0.375rem 0.5rem; border: 1px solid #cbd5e1; border-radius: 0.375rem; font-size: 0.875rem; text-align: center; }
                </style>
            </head>
            <body>
                <div class="container">
                    <h1>Fake-ID</h1>
                    <p class="subtitle">Selecciona un usuario para iniciar sesion</p>
                    <div class="org-field">
                        <label>Organization App ID:</label>
                        <input type="number" id="orgAppId" value="1" min="1" />
                    </div>
                    <div class="grid">
                        {{cards}}
                    </div>
                </div>
                <script>
                    document.querySelectorAll('.card').forEach(card => {
                        card.addEventListener('click', (e) => {
                            e.preventDefault();
                            const email = card.dataset.email;
                            const orgAppId = document.getElementById('orgAppId').value;
                            const redirectUri = '{{escapedRedirectUri}}';
                            window.location.href = redirectUri + '?code=' + encodeURIComponent(email) + '&organizationApplicationId=' + orgAppId + '&provider=fake';
                        });
                    });
                </script>
            </body>
            </html>
            """;
    }
}

public record DevTokenRequest(string? Email = null, int? OrganizationApplicationId = null);
