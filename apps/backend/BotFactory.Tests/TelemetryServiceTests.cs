using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class TelemetryServiceTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public TelemetryServiceTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task EmitAsync_PersistsRow_AndStampsOrganization()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();

        var org = $"tel-{Guid.NewGuid():N}";
        tenant.SetTenant(org);

        await telemetry.EmitAsync(new TelemetryEmit(
            "llm_started",
            EventSource.Backend,
            ConversationId: 999,
            Stage: "llm",
            LatencyMs: 100));

        var stored = await db.TelemetryEvents.IgnoreQueryFilters()
            .Where(e => e.OrganizationId == org)
            .ToListAsync();
        Assert.Single(stored);
        Assert.Equal("llm_started", stored[0].EventName);
        Assert.Equal("llm", stored[0].Stage);
        Assert.Equal(100, stored[0].LatencyMs);
        Assert.Equal(org, stored[0].OrganizationId);
    }

    [Fact]
    public async Task GetEventsAsync_Paginates_AndFiltersByName()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var org = $"tel-{Guid.NewGuid():N}";
        tenant.SetTenant(org);

        for (int i = 0; i < 3; i++)
            await telemetry.EmitAsync(new TelemetryEmit("llm_started", EventSource.Backend, ConversationId: 1));
        for (int i = 0; i < 2; i++)
            await telemetry.EmitAsync(new TelemetryEmit("kb_index_started", EventSource.Backend, BotId: 1));

        var (filtered, totalFiltered) = await telemetry.GetEventsAsync(
            eventNames: new[] { "llm_started" }, page: 1, pageSize: 50);
        Assert.Equal(3, totalFiltered);
        Assert.All(filtered, e => Assert.Equal("llm_started", e.EventName));

        var (paged, totalAll) = await telemetry.GetEventsAsync(page: 1, pageSize: 2);
        Assert.Equal(5, totalAll);
        Assert.Equal(2, paged.Count);
    }

    [Fact]
    public async Task GetEventsAsync_ScopedToTenant()
    {
        var orgA = $"tel-{Guid.NewGuid():N}";
        var orgB = $"tel-{Guid.NewGuid():N}";

        using (var scopeA = _factory.Services.CreateScope())
        {
            var tenant = scopeA.ServiceProvider.GetRequiredService<ITenantContextSetter>();
            var telemetry = scopeA.ServiceProvider.GetRequiredService<ITelemetryService>();
            tenant.SetTenant(orgA);
            await telemetry.EmitAsync(new TelemetryEmit("conversation_started", EventSource.Backend, ConversationId: 1));
        }

        using (var scopeB = _factory.Services.CreateScope())
        {
            var tenant = scopeB.ServiceProvider.GetRequiredService<ITenantContextSetter>();
            var telemetry = scopeB.ServiceProvider.GetRequiredService<ITelemetryService>();
            tenant.SetTenant(orgB);
            await telemetry.EmitAsync(new TelemetryEmit("conversation_started", EventSource.Backend, ConversationId: 2));

            var (items, _) = await telemetry.GetEventsAsync();
            Assert.All(items, e => Assert.Equal(orgB, e.OrganizationId));
            Assert.DoesNotContain(items, e => e.ConversationId == 1);
        }
    }

    [Fact]
    public void FeedAllowlist_ContainsLifecycleAndErrorEvents()
    {
        Assert.Contains("conversation_started", TelemetryService.FeedAllowlist);
        Assert.Contains("conversation_ended", TelemetryService.FeedAllowlist);
        Assert.Contains("llm_first_token", TelemetryService.FeedAllowlist);
        Assert.Contains("llm_usage", TelemetryService.FeedAllowlist);
        Assert.Contains("stt_failed", TelemetryService.FeedAllowlist);
        Assert.Contains("barge_in", TelemetryService.FeedAllowlist);
        // Session lifecycle + per-turn summary events surface in the feed.
        Assert.Contains("agent_state_changed", TelemetryService.FeedAllowlist);
        Assert.Contains("session_paused", TelemetryService.FeedAllowlist);
        Assert.Contains("session_resumed", TelemetryService.FeedAllowlist);
        Assert.Contains("turn_started", TelemetryService.FeedAllowlist);
        Assert.Contains("backend_completed", TelemetryService.FeedAllowlist);
        Assert.Contains("turn_completed", TelemetryService.FeedAllowlist);
        // Noisy per-stage/partial events should NOT be in the feed.
        Assert.DoesNotContain("metrics_collected", TelemetryService.FeedAllowlist);
        Assert.DoesNotContain("user_input_transcribed", TelemetryService.FeedAllowlist);
    }
}
