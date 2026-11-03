using BotFactory.Api.Helpers;

namespace BotFactory.Tests;

public class EmbedSpaShellFetcherTests
{
    [Theory]
    [InlineData("https://bot.example.go.cr/index.html", "ignored.example", "https://bot.example.go.cr/index.html")]
    [InlineData("https://bf.example.com/index.html", null, "https://bf.example.com/index.html")]
    [InlineData("http://bf-frontend/index.html", "bot.example.go.cr", "https://bot.example.go.cr/index.html")]
    [InlineData("https://bf-frontend/index.html", "bot.example.go.cr", "https://bot.example.go.cr/index.html")]
    [InlineData("", "bot.example.go.cr", "https://bot.example.go.cr/index.html")]
    [InlineData(null, "bot.example.go.cr", "https://bot.example.go.cr/index.html")]
    [InlineData("http://localhost:4200/index.html", "bf.example.com", "https://bf.example.com/index.html")]
    public void ResolveShellUrl_PrefersHttpsConfig_ElseRequestHost(
        string? spaIndexUrl, string? requestHost, string expected)
    {
        Assert.Equal(expected, EmbedSpaShellFetcher.ResolveShellUrl(spaIndexUrl, requestHost));
    }

    [Fact]
    public void ResolveShellUrl_NoConfigNoHost_ReturnsNull()
    {
        Assert.Null(EmbedSpaShellFetcher.ResolveShellUrl(null, null));
        Assert.Null(EmbedSpaShellFetcher.ResolveShellUrl("http://bf-frontend/index.html", null));
        Assert.Null(EmbedSpaShellFetcher.ResolveShellUrl("", "bf-frontend"));
    }

    [Theory]
    [InlineData("bf-frontend", true)]
    [InlineData("BF-Frontend", true)]
    [InlineData("bot.example.go.cr", false)]
    [InlineData(null, false)]
    public void IsDockerFrontendHost_MatchesComposeServiceName(string? host, bool expected)
    {
        Assert.Equal(expected, EmbedSpaShellFetcher.IsDockerFrontendHost(host));
    }

    [Fact]
    public async Task IsDockerFrontendReachableAsync_UnknownHost_ReturnsFalseQuickly()
    {
        // Azure / non-compose hosts: short DNS miss, must not hang the embed path.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reachable = await EmbedSpaShellFetcher.IsDockerFrontendReachableAsync();
        sw.Stop();

        Assert.False(reachable);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"DNS miss took {sw.Elapsed}; expected well under 3s");
    }
}
