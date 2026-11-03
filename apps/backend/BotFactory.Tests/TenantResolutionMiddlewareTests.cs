using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class TenantResolutionMiddlewareTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public TenantResolutionMiddlewareTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AuthenticatedSession_PopulatesTenantContextOnMe()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(
            _factory, email: "superadmin@example.com", organizationApplicationId: 42);

        var me = await client.GetFromJsonAsync<UserInfoResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Equal("00000000-0000-0000-0000-000000000001", me.UserId);
        Assert.Equal("Super Administrador", me.FullName);
        Assert.Equal("42", me.OrganizationId);
        Assert.Equal("Demo", me.OrganizationName);
        Assert.Contains(Permissions.SuperAdmin, me.Permissions);
    }

    [Fact]
    public async Task DifferentOrgApplicationId_ReflectedInTenantContext()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(
            _factory, email: "admin@example.com", organizationApplicationId: 999);

        var me = await client.GetFromJsonAsync<UserInfoResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Equal("999", me.OrganizationId);
        Assert.Contains(Permissions.BotAdmin, me.Permissions);
    }

    [Fact]
    public async Task TenantContext_DrivesOrgScopedDataAccess()
    {
        var org1 = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 301);
        var org2 = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 302);

        var create = await org1.PostAsJsonAsync("/api/bots", new
        {
            botName = "Tenant Middleware Bot",
            urlSlug = $"tenant-mw-{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var bot = await create.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(bot);

        var crossOrgGet = await org2.GetAsync($"/api/bots/{bot.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossOrgGet.StatusCode);
    }
}
