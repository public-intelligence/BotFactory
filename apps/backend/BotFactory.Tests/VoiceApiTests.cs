using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class VoiceApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public VoiceApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetElevenLabsVoices_WithoutApiKey_ReturnsNotConfigured()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var response = await client.GetAsync("/api/voices/elevenlabs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ElevenLabsVoicesResponse>();
        Assert.NotNull(payload);
        Assert.False(payload.Configured);
        Assert.Empty(payload.Voices);
        Assert.Null(payload.FetchedAt);
    }

    [Fact]
    public async Task GetElevenLabsVoices_RequiresAuthentication()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/voices/elevenlabs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
