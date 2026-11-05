using System.Net.Http.Json;
using System.Text.Json;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

/// <summary>
/// The history list and the conversation view show the tokens a conversation spent, but only to
/// whoever may read telemetry (the same people who see the events in the view).
/// </summary>
[Collection("Database")]
public class ConversationTokensApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ConversationTokensApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static JsonElement Usage(long prompt, long output, long cached = 0, bool reported = true) =>
        JsonSerializer.SerializeToElement(new
        {
            model = "m",
            call = 1,
            reported,
            complete = true,
            promptTokens = reported ? prompt : (long?)null,
            outputTokens = reported ? output : (long?)null,
            cachedTokens = reported ? cached : (long?)null,
            thoughtsTokens = reported ? 0 : (long?)null,
            totalTokens = reported ? prompt + output : (long?)null,
        });

    /// <summary>A bot with a conversation that spent tokens and one that never reached the model.</summary>
    private async Task<(int BotId, int Spent, int Quiet)> SeedAsync()
    {
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        Conversation NewConversation() => new()
        {
            BotId = bot.Id,
            UserId = $"anon:{Guid.NewGuid():N}",
            Status = ConversationStatus.Closed,
            AccessMode = ConversationAccessModes.Public,
            Surface = ConversationSurfaces.Direct,
        };
        var spent = NewConversation();
        var quiet = NewConversation();
        db.Conversations.AddRange(spent, quiet);
        await db.SaveChangesAsync();

        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
        foreach (var data in new[] { Usage(27_492, 22), Usage(31_018, 100, cached: 25_000), Usage(0, 0, reported: false) })
        {
            await telemetry.EmitAsync(new TelemetryEmit(
                ConversationTokenUsage.EventName, EventSource.Backend, BotId: bot.Id, ConversationId: spent.Id, Data: data));
        }

        return (bot.Id, spent.Id, quiet.Id);
    }

    [Fact]
    public async Task ConversationList_ForTelemetryReaders_CarriesTheSumOfTheTokensEachConversationSpent()
    {
        var (botId, spent, quiet) = await SeedAsync();
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "analyst@example.com");

        var page = await client.GetFromJsonAsync<PaginatedResponse<ConversationResponse>>($"/api/bots/{botId}/conversations");

        Assert.NotNull(page);
        var withTokens = Assert.Single(page.Items, c => c.Id == spent).Tokens;
        Assert.NotNull(withTokens);
        Assert.Equal(2, withTokens.Calls);
        Assert.Equal(58_510, withTokens.PromptTokens);
        Assert.Equal(25_000, withTokens.CachedTokens);
        Assert.Equal(122, withTokens.OutputTokens);
        Assert.Equal(58_632, withTokens.TotalTokens);
        // Never reached the model: no total, which is not a total of zero.
        Assert.Null(Assert.Single(page.Items, c => c.Id == quiet).Tokens);
    }

    [Fact]
    public async Task ConversationList_WithoutTelemetryRead_HasNoTokens()
    {
        var (botId, spent, _) = await SeedAsync();
        // The auditor reads conversations but not telemetry.
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "auditor@example.com");

        var page = await client.GetFromJsonAsync<PaginatedResponse<ConversationResponse>>($"/api/bots/{botId}/conversations");

        Assert.NotNull(page);
        Assert.Contains(page.Items, c => c.Id == spent);
        Assert.All(page.Items, c => Assert.Null(c.Tokens));
    }

    [Fact]
    public async Task ConversationDetail_ForTelemetryReaders_CarriesTheTotal_AndNotForOthers()
    {
        var (botId, spent, quiet) = await SeedAsync();

        var analyst = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "analyst@example.com");
        var detail = await analyst.GetFromJsonAsync<ConversationDetailResponse>(
            $"/api/bots/{botId}/conversations/{spent}?includeEvents=true");
        Assert.Equal(58_632, detail!.Tokens!.TotalTokens);
        Assert.Equal(2, detail.Tokens.Calls);

        var untouched = await analyst.GetFromJsonAsync<ConversationDetailResponse>($"/api/bots/{botId}/conversations/{quiet}");
        Assert.Null(untouched!.Tokens);

        var auditor = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "auditor@example.com");
        var hidden = await auditor.GetFromJsonAsync<ConversationDetailResponse>($"/api/bots/{botId}/conversations/{spent}");
        Assert.Null(hidden!.Tokens);
    }
}
