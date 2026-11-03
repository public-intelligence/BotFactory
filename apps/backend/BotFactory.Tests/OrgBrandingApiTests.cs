using System.Net;
using System.Net.Http.Json;
using System.Text;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class OrgBrandingApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public OrgBrandingApiTests(BotFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static MultipartFormDataContent CreatePngContent(string filename = "logo.png")
    {
        // Minimal 1x1 PNG
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        formData.Add(fileContent, "file", filename);
        return formData;
    }

    [Fact]
    public async Task GetBranding_Empty_ReturnsNoAssets()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 501);

        var branding = await client.GetFromJsonAsync<OrgBrandingResponse>("/api/settings/branding");
        Assert.NotNull(branding);
        Assert.False(branding.HasLogo);
        Assert.False(branding.HasFavicon);
        Assert.Null(branding.LogoFilename);
        Assert.Null(branding.FaviconFilename);
    }

    [Fact]
    public async Task LogoLifecycle_UploadDownloadDelete()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 502);

        using (var content = CreatePngContent("org-logo.png"))
        {
            var upload = await client.PostAsync("/api/settings/branding/logo", content);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var branding = await upload.Content.ReadFromJsonAsync<OrgBrandingResponse>();
            Assert.NotNull(branding);
            Assert.True(branding.HasLogo);
            Assert.Equal("org-logo.png", branding.LogoFilename);
            Assert.False(branding.HasFavicon);
        }

        var download = await client.GetAsync("/api/settings/branding/logo/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);

        var delete = await client.DeleteAsync("/api/settings/branding/logo");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var after = await client.GetFromJsonAsync<OrgBrandingResponse>("/api/settings/branding");
        Assert.NotNull(after);
        Assert.False(after.HasLogo);

        var missing = await client.GetAsync("/api/settings/branding/logo/download");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task FaviconLifecycle_AndPublicBySlugDownload()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 503);
        var slug = $"branding-fav-{Guid.NewGuid():N}"[..20];

        var createBot = await client.PostAsJsonAsync("/api/bots", new CreateBotRequest(
            BotName: "Branding Favicon Bot",
            UrlSlug: slug,
            MasterPrompt: "test"));
        Assert.Equal(HttpStatusCode.Created, createBot.StatusCode);
        var bot = await createBot.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(bot);

        var publish = await client.PostAsync($"/api/bots/{bot.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        string faviconVersion;
        using (var content = CreatePngContent("favicon.png"))
        {
            var upload = await client.PostAsync("/api/settings/branding/favicon", content);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var branding = await upload.Content.ReadFromJsonAsync<OrgBrandingResponse>();
            Assert.NotNull(branding);
            Assert.True(branding.HasFavicon);
            Assert.Equal("favicon.png", branding.FaviconFilename);
            Assert.False(string.IsNullOrWhiteSpace(branding.FaviconVersion));
            faviconVersion = branding.FaviconVersion!;
        }

        var authDownload = await client.GetAsync("/api/settings/branding/favicon/download");
        Assert.Equal(HttpStatusCode.OK, authDownload.StatusCode);
        Assert.Equal("image/png", authDownload.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(authDownload.Headers.CacheControl);
        Assert.True(authDownload.Headers.CacheControl.Private);
        Assert.True(authDownload.Headers.CacheControl.NoCache);

        var anon = _factory.CreateClient();
        var publicDownload = await anon.GetAsync($"/api/bots/by-slug/{slug}/branding/favicon/download");
        Assert.Equal(HttpStatusCode.OK, publicDownload.StatusCode);
        Assert.Equal("image/png", publicDownload.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await publicDownload.Content.ReadAsByteArrayAsync());
        Assert.NotNull(publicDownload.Headers.CacheControl);
        Assert.True(publicDownload.Headers.CacheControl.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), publicDownload.Headers.CacheControl.MaxAge);
        var publicETag = publicDownload.Headers.ETag;
        Assert.NotNull(publicETag);

        // Anonymous conditional revalidation: same ETag → 304 with no body.
        using (var conditional = new HttpRequestMessage(
            HttpMethod.Get, $"/api/bots/by-slug/{slug}/branding/favicon/download"))
        {
            conditional.Headers.IfNoneMatch.Add(publicETag);
            var notModified = await anon.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
        }

        // Anonymous + current version → cacheable for a year, and shareable:
        // this asset is public by definition.
        var versioned = await anon.GetAsync(
            $"/api/bots/by-slug/{slug}/branding/favicon/download?v={faviconVersion}");
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);
        Assert.True(versioned.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromDays(365), versioned.Headers.CacheControl.MaxAge);

        var delete = await client.DeleteAsync("/api/settings/branding/favicon");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missingPublic = await anon.GetAsync($"/api/bots/by-slug/{slug}/branding/favicon/download");
        Assert.Equal(HttpStatusCode.NotFound, missingPublic.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsInvalidExtension()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 504);

        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not-an-image"));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        formData.Add(fileContent, "file", "logo.txt");

        var response = await client.PostAsync("/api/settings/branding/logo", formData);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_OrgCannotSeeOtherOrgLogo()
    {
        var org1 = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 505);
        var org2 = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 506);

        using (var content = CreatePngContent("org1-logo.png"))
        {
            var upload = await org1.PostAsync("/api/settings/branding/logo", content);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        var org2Branding = await org2.GetFromJsonAsync<OrgBrandingResponse>("/api/settings/branding");
        Assert.NotNull(org2Branding);
        Assert.False(org2Branding.HasLogo);

        var org2Download = await org2.GetAsync("/api/settings/branding/logo/download");
        Assert.Equal(HttpStatusCode.NotFound, org2Download.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/settings/branding");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
