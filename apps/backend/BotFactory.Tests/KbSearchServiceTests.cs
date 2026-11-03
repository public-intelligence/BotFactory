using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class KbSearchServiceTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public KbSearchServiceTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchAsync_WhenNoChunks_ReturnsEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var kb = scope.ServiceProvider.GetRequiredService<KbSearchService>();

        var bot = new Bot
        {
            BotName = "Kb Search Empty",
            UrlSlug = $"kbse-{Guid.NewGuid():N}",
            PublishedState = "draft",
            MasterPrompt = "x",
        };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var results = await kb.SearchAsync(bot.Id, "no matching chunks expected", topK: 5, maxDistance: 0.99, CancellationToken.None);

        Assert.Empty(results);
    }
}
