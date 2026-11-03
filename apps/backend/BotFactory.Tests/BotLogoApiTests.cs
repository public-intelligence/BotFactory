using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class BotLogoApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public BotLogoApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static MultipartFormDataContent CreatePngContent(string filename = "bot-logo.png")
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        formData.Add(fileContent, "file", filename);
        return formData;
    }

    private async Task<int> CreateBotAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Logo Test Bot",
            urlSlug = $"logo-{Guid.NewGuid():N}",
        });
        response.EnsureSuccessStatusCode();
        var bot = await response.Content.ReadFromJsonAsync<BotResponse>();
        return bot!.Id;
    }

    [Fact]
    public async Task LogoLifecycle_UploadDownloadDelete()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await CreateBotAsync(client);
        string logoVersion;

        using (var content = CreatePngContent())
        {
            var upload = await client.PostAsync($"/api/bots/{botId}/logo", content);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var bot = await upload.Content.ReadFromJsonAsync<BotResponse>();
            Assert.NotNull(bot);
            Assert.True(bot.HasCustomLogo);
            Assert.Equal("bot-logo.png", bot.LogoFilename);
            Assert.False(string.IsNullOrWhiteSpace(bot.LogoVersion));
            logoVersion = bot.LogoVersion!;
        }

        var download = await client.GetAsync($"/api/bots/{botId}/logo/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());
        Assert.NotNull(download.Headers.CacheControl);
        Assert.True(download.Headers.CacheControl.Private);
        Assert.True(download.Headers.CacheControl.NoCache);
        var etag = download.Headers.ETag;
        Assert.NotNull(etag);

        // Conditional revalidation: same ETag → 304 with no body.
        using (var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/bots/{botId}/logo/download"))
        {
            conditional.Headers.IfNoneMatch.Add(etag);
            var notModified = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
        }

        // A request naming the current version may be cached for a year; the scope
        // stays private, since this is a tenant's asset.
        var versioned = await client.GetAsync($"/api/bots/{botId}/logo/download?v={logoVersion}");
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);
        Assert.NotNull(versioned.Headers.CacheControl);
        Assert.True(versioned.Headers.CacheControl.Private);
        Assert.False(versioned.Headers.CacheControl.NoCache);
        Assert.Equal(TimeSpan.FromDays(365), versioned.Headers.CacheControl.MaxAge);

        // A stale version must never be pinned: it still serves the current asset,
        // but only under revalidation.
        var stale = await client.GetAsync($"/api/bots/{botId}/logo/download?v=deadbeef");
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.NotNull(stale.Headers.CacheControl);
        Assert.True(stale.Headers.CacheControl.NoCache);
        Assert.Null(stale.Headers.CacheControl.MaxAge);

        // Re-uploading mints a new version, so the old URL stops being current.
        using (var content = CreatePngContent())
        {
            var reupload = await client.PostAsync($"/api/bots/{botId}/logo", content);
            Assert.Equal(HttpStatusCode.OK, reupload.StatusCode);
            var bot = await reupload.Content.ReadFromJsonAsync<BotResponse>();
            Assert.NotNull(bot);
            Assert.NotEqual(logoVersion, bot.LogoVersion);
        }

        var supersededed = await client.GetAsync($"/api/bots/{botId}/logo/download?v={logoVersion}");
        Assert.Equal(HttpStatusCode.OK, supersededed.StatusCode);
        Assert.True(supersededed.Headers.CacheControl!.NoCache);

        var delete = await client.DeleteAsync($"/api/bots/{botId}/logo");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/bots/{botId}/logo/download");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Upload_InvalidExtension_Returns400()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await CreateBotAsync(client);

        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("not an image"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        formData.Add(fileContent, "file", "notes.txt");

        var upload = await client.PostAsync($"/api/bots/{botId}/logo", formData);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task Upload_MissingBot_Returns404()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        using var content = CreatePngContent();
        var upload = await client.PostAsync("/api/bots/999999/logo", content);
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
    }
}
