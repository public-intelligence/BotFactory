using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests.Infrastructure;

public sealed record PublicBotSession(
    Bot Bot,
    int ConversationId,
    string AccessToken,
    DateTime AccessTokenExpiresAt);

public static class PublicBotTestHelper
{
    /// <summary>
    /// Seeds a published public bot (RequiresAuthentication=false) under the super-admin org.
    /// </summary>
    public static async Task<Bot> SeedPublishedPublicBotAsync(
        WebApplicationFactory<Program> factory,
        Action<Bot>? configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Public Test Bot",
            UrlSlug = $"pub-{Guid.NewGuid():N}",
            PublicId = PublicIdGenerator.NewId(),
            PublishedState = BotStates.Published,
            RequiresAuthentication = false,
            EnableChat = true,
            EnableVoice = false,
            MasterPrompt = "You are a public test bot.",
        };
        configure?.Invoke(bot);
        db.Bots.Add(bot);
        await db.SaveChangesAsync();
        return bot;
    }

    /// <summary>
    /// POST /api/public/bots/{publicId}/chat and return the minted anonymous session.
    /// </summary>
    public static async Task<PublicBotSession> StartPublicChatAsync(
        WebApplicationFactory<Program> factory,
        Bot bot,
        string? mode = "text",
        object? body = null)
    {
        using var client = factory.CreateClient();
        var url = $"/api/public/bots/{bot.PublicId}/chat";
        if (!string.IsNullOrEmpty(mode))
            url += $"?mode={Uri.EscapeDataString(mode)}";

        var response = body is null
            ? await client.PostAsync(url, content: null)
            : await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<PublicChatSessionResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));

        return new PublicBotSession(bot, session.ConversationId, session.AccessToken, session.AccessTokenExpiresAt);
    }

    /// <summary>
    /// Seeds a published public bot and starts an anonymous chat session.
    /// </summary>
    public static async Task<PublicBotSession> SeedAndStartAsync(
        WebApplicationFactory<Program> factory,
        Action<Bot>? configure = null,
        string? mode = "text")
    {
        var bot = await SeedPublishedPublicBotAsync(factory, configure);
        return await StartPublicChatAsync(factory, bot, mode);
    }

    /// <summary>
    /// HttpClient that sends the anonymous public Bearer token.
    /// </summary>
    public static HttpClient CreatePublicClient(
        WebApplicationFactory<Program> factory,
        string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
