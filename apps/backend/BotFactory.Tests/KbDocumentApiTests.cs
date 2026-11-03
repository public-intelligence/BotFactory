using System.Net;
using System.Net.Http.Json;
using System.Text;
using BotFactory.Core.DTOs;
using BotFactory.Infrastructure.Services;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class KbDocumentApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public KbDocumentApiTests(BotFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static MultipartFormDataContent CreateTxtFileContent(string filename = "test.txt", string content = "Hello world")
    {
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        formData.Add(fileContent, "file", filename);
        return formData;
    }

    private async Task<int> GetOrCreateBotIdAsync(HttpClient client)
    {
        var botsResponse = await client.GetFromJsonAsync<PaginatedResponse<BotResponse>>("/api/bots");
        if (botsResponse?.Items.Count > 0)
            return botsResponse.Items[0].Id;

        var createResponse = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "KB Test Bot",
            urlSlug = $"kb-test-{Guid.NewGuid():N}",
            masterPrompt = "test"
        });
        createResponse.EnsureSuccessStatusCode();
        var bot = await createResponse.Content.ReadFromJsonAsync<BotResponse>();
        return bot!.Id;
    }

    [Fact]
    public async Task UploadAndListDocuments()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        // Upload
        using var formData = CreateTxtFileContent("politica-privacidad.txt", "Privacy policy content");
        var uploadResponse = await client.PostAsync($"/api/bots/{botId}/documents", formData);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var doc = await uploadResponse.Content.ReadFromJsonAsync<KbDocumentResponse>();
        Assert.NotNull(doc);
        Assert.Equal("politica-privacidad.txt", doc.Filename);
        Assert.Equal("Pending", doc.Status);
        Assert.Equal("File", doc.SourceType);
        Assert.Null(doc.SourceUrl);
        Assert.Equal(botId, doc.BotId);

        // List
        var listResponse = await client.GetFromJsonAsync<PaginatedResponse<KbDocumentResponse>>($"/api/bots/{botId}/documents");
        Assert.NotNull(listResponse);
        Assert.True(listResponse.TotalItems >= 1);
        Assert.Contains(listResponse.Items, d => d.Filename == "politica-privacidad.txt");
    }

    [Fact]
    public async Task CreateFromUrls_CreatesPendingUrlDocuments()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        var response = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[]
            {
                "https://example.com/a",
                "https://example.com/a", // duplicate
                "https://example.com/b",
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CreateKbDocumentsFromUrlsResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Created.Count);
        Assert.Equal(0, payload.SkippedExistingCount);
        Assert.All(payload.Created, d =>
        {
            Assert.Equal("Url", d.SourceType);
            Assert.Equal("Pending", d.Status);
            Assert.False(string.IsNullOrWhiteSpace(d.SourceUrl));
            Assert.Equal(d.SourceUrl, d.Filename);
        });
    }

    [Fact]
    public async Task CreateFromUrls_SkipsUrlsAlreadyPresentForBot()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);
        var url = $"https://example.com/already-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[] { url }
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[] { url, $"https://example.com/new-{Guid.NewGuid():N}" }
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var payload = await second.Content.ReadFromJsonAsync<CreateKbDocumentsFromUrlsResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.Created.Count);
        Assert.Equal(1, payload.SkippedExistingCount);
        Assert.DoesNotContain(payload.Created, d => string.Equals(d.SourceUrl, url, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateFromUrls_RejectsPrivateHost()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        var response = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[] { "http://127.0.0.1/secret" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateFromUrls_RejectsPdfUrl_WhenOnlyPdfs()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        var response = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[] { "https://example.com/salarios-minimos-2023.pdf" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal(KbUrlClassifier.PdfNotSupportedMessage, body!["error"]);
    }

    [Fact]
    public async Task CreateFromUrls_SkipsPdfAndCreatesHtmlUrls()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        var response = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[]
            {
                "https://example.com/page",
                "https://example.com/salarios-minimos-2023.pdf",
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CreateKbDocumentsFromUrlsResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Created);
        Assert.Equal(1, payload.SkippedPdfCount);
        Assert.Equal("https://example.com/page", payload.Created[0].SourceUrl);
    }

    [Fact]
    public async Task CreateFromUrls_RejectsOverCap()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        var urls = Enumerable.Range(0, 5001).Select(i => $"https://example.com/page-{i}").ToArray();
        var response = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new { urls });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDocument()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        // Upload
        using var formData = CreateTxtFileContent("to-delete.txt");
        var uploadResponse = await client.PostAsync($"/api/bots/{botId}/documents", formData);
        var doc = await uploadResponse.Content.ReadFromJsonAsync<KbDocumentResponse>();

        // Delete
        var deleteResponse = await client.DeleteAsync($"/api/bots/{botId}/documents/{doc!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify deleted
        var getResponse = await client.GetAsync($"/api/bots/{botId}/documents/{doc.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task ListDocuments_FiltersByStatus()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        using var formData = CreateTxtFileContent($"filter-status-{Guid.NewGuid():N}.txt", "content for filter");
        var uploadResponse = await client.PostAsync($"/api/bots/{botId}/documents", formData);
        uploadResponse.EnsureSuccessStatusCode();

        var filtered = await client.GetFromJsonAsync<PaginatedResponse<KbDocumentResponse>>(
            $"/api/bots/{botId}/documents?status=Pending&pageSize=100");
        Assert.NotNull(filtered);
        Assert.All(filtered.Items, d => Assert.Equal("Pending", d.Status));
    }

    [Fact]
    public async Task BulkDelete_ByStatus_DeletesMatchingDocuments()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        var createResponse = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[]
            {
                $"https://example.com/bulk-del-{Guid.NewGuid():N}",
                $"https://example.com/bulk-del-{Guid.NewGuid():N}",
            }
        });
        createResponse.EnsureSuccessStatusCode();
        var payload = await createResponse.Content.ReadFromJsonAsync<CreateKbDocumentsFromUrlsResponse>();
        var docs = payload!.Created;
        Assert.Equal(2, docs.Count);

        var bulkResponse = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/bulk-delete", new
        {
            documentIds = docs.Select(d => d.Id).ToArray()
        });
        Assert.Equal(HttpStatusCode.OK, bulkResponse.StatusCode);
        var result = await bulkResponse.Content.ReadFromJsonAsync<BulkKbDocumentsResponse>();
        Assert.Equal(2, result!.AffectedCount);

        foreach (var doc in docs)
        {
            var getResponse = await client.GetAsync($"/api/bots/{botId}/documents/{doc.Id}");
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        }
    }

    [Fact]
    public async Task UploadNonTxtFile_Returns400()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var botId = await GetOrCreateBotIdAsync(client);

        using var formData = CreateTxtFileContent("document.pdf", "not a real pdf");
        var response = await client.PostAsync($"/api/bots/{botId}/documents", formData);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadToNonExistentBot_Returns404()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        using var formData = CreateTxtFileContent();
        var response = await client.PostAsync("/api/bots/99999/documents", formData);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_OrgCannotAccessOtherOrgDocuments()
    {
        var org1Client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 500);
        var org2Client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 600);

        // Org 1 creates a bot and uploads a document
        var createBotResponse = await org1Client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Org1 KB Bot",
            urlSlug = $"org1-kb-{Guid.NewGuid():N}",
            masterPrompt = "test"
        });
        var bot = await createBotResponse.Content.ReadFromJsonAsync<BotResponse>();

        using var formData = CreateTxtFileContent("org1-secret.txt");
        await org1Client.PostAsync($"/api/bots/{bot!.Id}/documents", formData);

        // Org 2 cannot see Org 1's bot or documents
        var org2Response = await org2Client.GetAsync($"/api/bots/{bot.Id}/documents");
        Assert.Equal(HttpStatusCode.NotFound, org2Response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/bots/1/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
