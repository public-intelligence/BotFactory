using BotFactory.KbScraper;
using BotFactory.KbScraper.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;

namespace BotFactory.KbScraper.Tests;

public sealed class MockRenderService : IRenderService
{
    public RenderRequest? LastRequest { get; private set; }
    public (bool Success, RenderResponse? Response, RenderErrorResponse? Error) NextResult { get; set; } =
        (true, new RenderResponse("<html>ok</html>", "https://example.test/final", 200, 42), null);

    public Task<(bool Success, RenderResponse? Response, RenderErrorResponse? Error)> RenderAsync(
        RenderRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(NextResult);
    }
}

public class KbScraperWebApplicationFactory : WebApplicationFactory<Program>
{
    public MockRenderService MockRenderer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("API_KEY", "test-scraper-key");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<PlaywrightRenderService>();
            services.RemoveAll<IRenderService>();
            services.AddSingleton<IRenderService>(MockRenderer);
        });
    }
}

public class KbScraperApiTests : IClassFixture<KbScraperWebApplicationFactory>
{
    private readonly KbScraperWebApplicationFactory _factory;

    public KbScraperApiTests(KbScraperWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Render_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/render", new RenderRequest("https://example.test"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Render_WithAuth_UsesMockRenderer()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-scraper-key");

        var response = await client.PostAsJsonAsync("/render", new RenderRequest("https://example.test/page"));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RenderResponse>();
        Assert.NotNull(payload);
        Assert.Equal("<html>ok</html>", payload.Html);
        Assert.Equal("https://example.test/page", _factory.MockRenderer.LastRequest?.Url);
    }

    [Fact]
    public async Task Render_EmptyUrl_Returns400()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-scraper-key");

        var response = await client.PostAsJsonAsync("/render", new RenderRequest(""));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Render_Busy_Returns503()
    {
        _factory.MockRenderer.NextResult = (
            false,
            null,
            new RenderErrorResponse("busy", "Scraper is at capacity."));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-scraper-key");

        var response = await client.PostAsJsonAsync("/render", new RenderRequest("https://example.test"));
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
