using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BotFactory.Api.Endpoints;
using BotFactory.Core.Auth;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class AuthEndpointsTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public AuthEndpointsTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Providers_IncludesGobIdAndFakeInDevelopment()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = await response.Content.ReadFromJsonAsync<List<AuthProviderInfo>>();
        Assert.NotNull(providers);
        Assert.Contains(providers, p => p.Name == "gobid");
        Assert.Contains(providers, p => p.Name == "fake");
        var fake = providers.Single(p => p.Name == "fake");
        Assert.Contains("/api/auth/dev/login", fake.Url);
    }

    [Fact]
    public async Task Login_ReturnsUrlForFakeAndGobId()
    {
        using var client = _factory.CreateClient();

        var fake = await client.GetFromJsonAsync<LoginUrlResponse>("/api/auth/login?provider=fake");
        Assert.NotNull(fake);
        Assert.Contains("/api/auth/dev/login", fake.Url);

        var gobid = await client.GetFromJsonAsync<LoginUrlResponse>("/api/auth/login?provider=gobid");
        Assert.NotNull(gobid);
        Assert.False(string.IsNullOrWhiteSpace(gobid.Url));
    }

    [Fact]
    public async Task TokenExchange_Fake_SetsCookieAndMeWorks()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var exchange = await client.PostAsJsonAsync("/api/auth/token", new TokenExchangeBody(
            Code: "superadmin@example.com",
            OrganizationApplicationId: 1,
            Provider: "fake"));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal("00000000-0000-0000-0000-000000000001", doc.RootElement.GetProperty("userId").GetString());
        Assert.Equal("1", doc.RootElement.GetProperty("organizationId").GetString());
    }

    [Fact]
    public async Task Logout_ClearsSession()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var before = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var after = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task HubToken_ReturnsSessionGuid()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var response = await client.GetAsync("/api/auth/hub-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadAsStringAsync();
        Assert.True(Guid.TryParse(token, out _));
    }

    [Fact]
    public async Task HubToken_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/hub-token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenExchange_UnknownFakeUser_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new TokenExchangeBody(
            Code: "missing@example.com",
            OrganizationApplicationId: 1,
            Provider: "fake"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
