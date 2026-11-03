using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class BotThinkingSoundApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public BotThinkingSoundApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static MultipartFormDataContent CreateMp3Content(string filename = "thinking.mp3")
        => CreateMp3Content([0xFF, 0xFB, 0x90, 0x00], filename);

    private static MultipartFormDataContent CreateMp3Content(byte[] bytes, string filename = "thinking.mp3")
    {
        // Minimal bytes — endpoint validates extension and size only.
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
        formData.Add(fileContent, "file", filename);
        return formData;
    }

    private async Task<int> CreateBotAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Thinking Sound Bot",
            urlSlug = $"think-{Guid.NewGuid():N}",
        });
        response.EnsureSuccessStatusCode();
        var bot = await response.Content.ReadFromJsonAsync<BotResponse>();
        return bot!.Id;
    }

    [Fact]
    public async Task ThinkingSoundLifecycle_UploadDownloadDelete()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await CreateBotAsync(client);

        using (var content = CreateMp3Content())
        {
            var upload = await client.PostAsync($"/api/bots/{botId}/thinking-sound", content);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var bot = await upload.Content.ReadFromJsonAsync<BotResponse>();
            Assert.NotNull(bot);
            Assert.True(bot.HasCustomThinkingSound);
            Assert.Equal("thinking.mp3", bot.ThinkingSoundFilename);
        }

        var download = await client.GetAsync($"/api/bots/{botId}/thinking-sound/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("audio/mpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());

        var delete = await client.DeleteAsync($"/api/bots/{botId}/thinking-sound");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/bots/{botId}/thinking-sound/download");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>
    /// Safari treats byte-range support as a precondition for playing media: it
    /// probes with a Range request and can refuse to play when the server answers
    /// 200 with the whole body. The SAS URLs this endpoint replaced pointed at
    /// storage, which honours Range natively, so proxying the bytes through the
    /// API is what put it at risk. We never seek in a thinking sound ourselves —
    /// this exists purely so that probe gets a real answer.
    /// </summary>
    [Fact]
    public async Task ThinkingSoundDownload_ServesRangeRequests()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var create = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Range Bot",
            urlSlug = $"range-{Guid.NewGuid():N}",
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<BotResponse>();
        Assert.NotNull(created);
        var botId = created.Id;
        var slug = created.UrlSlug;

        var publish = await client.PostAsync($"/api/bots/{botId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        // Long enough that a partial response is distinguishable from the whole.
        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        using (var content = CreateMp3Content(payload))
        {
            var upload = await client.PostAsync($"/api/bots/{botId}/thinking-sound", content);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        // The unconditional response must advertise the capability — this header is
        // what a probing client looks for.
        var full = await client.GetAsync($"/api/bots/{botId}/thinking-sound/download");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Contains("bytes", full.Headers.AcceptRanges);
        Assert.Equal(payload, await full.Content.ReadAsByteArrayAsync());

        using (var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/bots/{botId}/thinking-sound/download"))
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
            var partial = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
            var contentRange = partial.Content.Headers.ContentRange;
            Assert.NotNull(contentRange);
            Assert.Equal("bytes", contentRange.Unit);
            Assert.Equal(0, contentRange.From);
            Assert.Equal(9, contentRange.To);
            Assert.Equal(payload.Length, contentRange.Length);
            Assert.Equal(payload.Take(10), await partial.Content.ReadAsByteArrayAsync());
        }

        // A range past the end is unsatisfiable rather than a silent whole-body 200.
        using (var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/bots/{botId}/thinking-sound/download"))
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(9000, 9100);
            var unsatisfiable = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, unsatisfiable.StatusCode);
        }

        // The anonymous route public chat actually plays from must opt in too.
        var anon = _factory.CreateClient();
        using (var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/bots/by-slug/{slug}/thinking-sound/download"))
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
            var partial = await anon.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
            Assert.Equal(payload.Take(10), await partial.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task Upload_NonMp3_Returns400()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await CreateBotAsync(client);

        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0x00, 0x01]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        formData.Add(fileContent, "file", "thinking.wav");

        var upload = await client.PostAsync($"/api/bots/{botId}/thinking-sound", formData);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }
}
