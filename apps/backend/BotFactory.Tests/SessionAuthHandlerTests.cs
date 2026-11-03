using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BotFactory.Infrastructure.Auth;
using BotFactory.Infrastructure.Data;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class SessionAuthHandlerTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public SessionAuthHandlerTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CookieAuth_AllowsMe()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BearerSessionGuid_AllowsMe()
    {
        var cookieClient = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var hubToken = await cookieClient.GetStringAsync("/api/auth/hub-token");

        using var bearerClient = _factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hubToken);
        var response = await bearerClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task QueryAccessToken_OnHubPath_Authenticates()
    {
        var cookieClient = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var hubToken = await cookieClient.GetStringAsync("/api/auth/hub-token");

        await using var hub = await HubTestHelper.StartAsync(_factory, hubToken);
        Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected, hub.State);
        await hub.StopAsync();
    }

    [Fact]
    public async Task PublicJwt_AllowsPublicApi_AndForbiddenElsewhere()
    {
        var session = await PublicBotTestHelper.SeedAndStartAsync(_factory);
        using var client = PublicBotTestHelper.CreatePublicClient(_factory, session.AccessToken);

        var publicOk = await client.GetAsync(
            $"/api/public/bots/{session.Bot.PublicId}/conversations/{session.ConversationId}");
        Assert.Equal(HttpStatusCode.OK, publicOk.StatusCode);

        var botsForbid = await client.GetAsync("/api/bots");
        Assert.Equal(HttpStatusCode.Forbidden, botsForbid.StatusCode);

        var meForbid = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, meForbid.StatusCode);
    }

    [Fact]
    public async Task ProviderJwtPresentedDirectly_IsRejected()
    {
        // Mint a FakeId JWT (not a public token) and present it as Bearer — must fail.
        using var scope = _factory.Services.CreateScope();
        var fake = scope.ServiceProvider.GetRequiredService<FakeAuthProvider>();
        var tokens = await fake.ExchangeCodeAsync("public@example.com", "dev", 1);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingSession_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Guid.NewGuid().ToString());
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredSession_RefreshesViaFakeProvider()
    {
        // Create a real session, then force ExpiresAt into the past while keeping a valid refresh token.
        var cookieClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var create = await cookieClient.PostAsJsonAsync("/api/auth/dev/token", new
        {
            email = "analyst@example.com",
            organizationApplicationId = 1,
        });
        create.EnsureSuccessStatusCode();

        Guid sessionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            var session = await db.Sessions.OrderByDescending(s => s.CreatedAt).FirstAsync();
            sessionId = session.Id;
            session.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        // Cookie still present; next authenticated call should refresh transparently.
        var me = await cookieClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var verify = _factory.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var refreshed = await vdb.Sessions.FindAsync(sessionId);
        Assert.NotNull(refreshed);
        Assert.True(refreshed.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task ExpiredSession_RefreshFailure_RemovesSession()
    {
        var cookieClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var create = await cookieClient.PostAsJsonAsync("/api/auth/dev/token", new
        {
            email = "auditor@example.com",
            organizationApplicationId = 1,
        });
        create.EnsureSuccessStatusCode();

        Guid sessionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            var session = await db.Sessions.OrderByDescending(s => s.CreatedAt).FirstAsync();
            sessionId = session.Id;
            session.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            session.RefreshToken = "definitely-invalid-refresh-token";
            await db.SaveChangesAsync();
        }

        var me = await cookieClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var verify = _factory.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        Assert.Null(await vdb.Sessions.FindAsync(sessionId));
    }
}
