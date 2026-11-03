using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class TelemetryApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public TelemetryApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Analyst_CanListTelemetryEvents()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
            tenant.SetTenant(DbSeeder.SuperAdminOrgId);
            var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
            await telemetry.EmitAsync(new TelemetryEmit("llm_started", EventSource.Backend, ConversationId: 1));
        }

        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "analyst@example.com");
        var response = await client.GetFromJsonAsync<PaginatedResponse<TelemetryEventResponse>>(
            "/api/telemetry/events?eventNames=llm_started");

        Assert.NotNull(response);
        Assert.Contains(response.Items, e => e.EventName == "llm_started");
    }

    [Fact]
    public async Task Auditor_WithoutTelemetryRead_Forbidden()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "auditor@example.com");
        var response = await client.GetAsync("/api/telemetry/events");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicUsage_ReturnsAggregates()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
            tenant.SetTenant(DbSeeder.SuperAdminOrgId);
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();

            var bot = new Bot
            {
                BotName = "Telemetry Usage Bot",
                UrlSlug = $"tel-usage-{Guid.NewGuid():N}",
                PublishedState = "draft",
                MasterPrompt = "x",
            };
            db.Bots.Add(bot);
            await db.SaveChangesAsync();
            db.Conversations.Add(new Conversation
            {
                BotId = bot.Id,
                UserId = "anon:1",
                AccessMode = ConversationAccessModes.Public,
                Surface = ConversationSurfaces.Direct,
            });
            await db.SaveChangesAsync();
        }

        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "analyst@example.com");
        var usage = await client.GetFromJsonAsync<PublicUsageResponse>("/api/telemetry/public-usage");

        Assert.NotNull(usage);
        Assert.True(usage.PublicConversations >= 1);
    }
}
