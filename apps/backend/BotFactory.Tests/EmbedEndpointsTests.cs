using System.Net;
using BotFactory.Core.Entities;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BotFactory.Tests;

/// <summary>
/// Uses LiveKitTurn factory so <c>WithWebHostBuilder</c> dispose does not tear down
/// the shared Database collection SQL container.
/// </summary>
[Collection("LiveKitTurn")]
public class EmbedEndpointsTests : IDisposable
{
    private readonly LiveKitWebApplicationFactory _factory;
    private readonly string _webRoot;

    public EmbedEndpointsTests(LiveKitWebApplicationFactory factory)
    {
        _factory = factory;
        _webRoot = Path.Combine(Path.GetTempPath(), "bf-embed-wwwroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(
            Path.Combine(_webRoot, "index.html"),
            "<!doctype html><html><head><title>embed-shell</title></head><body>embed-ok</body></html>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_webRoot, recursive: true); } catch { /* best-effort */ }
    }

    private WebApplicationFactory<Program> WithLocalShell() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PublicAccess:SpaIndexUrl", "");
            builder.UseWebRoot(_webRoot);
        });

    [Fact]
    public async Task Embed_PublishedPublicEmbedEnabled_ReturnsHtmlAndCsp()
    {
        using var app = WithLocalShell();
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(app, b =>
        {
            b.EmbedEnabled = true;
            b.AllowedEmbedOrigins = "https://portal.gob.do\nhttps://www.example.gob.do";
        });

        using var client = app.CreateClient();
        var response = await client.GetAsync($"/api/embed/{bot.PublicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("embed-ok", html, StringComparison.Ordinal);

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var cspValues));
        var csp = Assert.Single(cspValues);
        Assert.StartsWith("frame-ancestors ", csp);
        Assert.Contains("'self'", csp);
        Assert.Contains("https://portal.gob.do", csp);
        Assert.Contains("https://www.example.gob.do", csp);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task Embed_EmptyAllowlist_FrameAncestorsSelfOnly()
    {
        using var app = WithLocalShell();
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(app, b =>
        {
            b.EmbedEnabled = true;
            b.AllowedEmbedOrigins = "";
        });

        using var client = app.CreateClient();
        var response = await client.GetAsync($"/api/embed/{bot.PublicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var cspValues));
        Assert.Equal("frame-ancestors 'self'", Assert.Single(cspValues));
    }

    [Fact]
    public async Task Embed_PrivateDraftOrDisabled_Returns404()
    {
        using var app = WithLocalShell();

        var privateBot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(app, b =>
        {
            b.RequiresAuthentication = true;
            b.EmbedEnabled = true;
        });
        var draftBot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(app, b =>
        {
            b.PublishedState = BotStates.Draft;
            b.EmbedEnabled = true;
        });
        var disabled = await PublicBotTestHelper.SeedPublishedPublicBotAsync(app, b =>
        {
            b.EmbedEnabled = false;
        });

        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/embed/{privateBot.PublicId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/embed/{draftBot.PublicId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/embed/{disabled.PublicId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/embed/missingPublicIdXXXXXXXX")).StatusCode);
    }

    [Fact]
    public async Task Embed_ShellUnavailable_Returns503()
    {
        var emptyRoot = Path.Combine(Path.GetTempPath(), "bf-embed-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);

        try
        {
            using var app = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("PublicAccess:SpaIndexUrl", "");
                builder.UseWebRoot(emptyRoot);
            });

            var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(app, b => b.EmbedEnabled = true);
            using var client = app.CreateClient();
            var response = await client.GetAsync($"/api/embed/{bot.PublicId}");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            try { Directory.Delete(emptyRoot, recursive: true); } catch { /* best-effort */ }
        }
    }
}
