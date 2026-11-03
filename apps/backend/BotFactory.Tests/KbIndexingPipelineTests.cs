using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services.Jobs;
using BotFactory.Core.Services;
using BotFactory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace BotFactory.Tests;

[Collection("Database")]
public class KbIndexingPipelineTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public KbIndexingPipelineTests(BotFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, int BotId)> SetupAsync()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        // Seed an embeddings API key so the orchestrator will enqueue jobs
        await client.PutAsJsonAsync("/api/settings", new
        {
            settings = new[] { new { serviceType = "Embeddings", apiKey = "fake-test-key-12345" } }
        });

        // Create a bot
        var createResponse = await client.PostAsJsonAsync("/api/bots", new
        {
            botName = "Indexing Test Bot",
            urlSlug = $"idx-test-{Guid.NewGuid():N}",
            masterPrompt = "test"
        });
        createResponse.EnsureSuccessStatusCode();
        var bot = await createResponse.Content.ReadFromJsonAsync<BotResponse>();

        return (client, bot!.Id);
    }

    private async Task RunIndexJobAsync(int documentId, string organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<IndexDocumentJob>();
        await job.ExecuteAsync(documentId, organizationId);
    }

    private async Task RunIngestJobAsync(int documentId, string organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<IngestUrlJob>();
        await job.ExecuteAsync(documentId, organizationId);
    }

    [Fact]
    public async Task UploadAndIndex_CreatesChunksWithEmbeddings()
    {
        var (client, botId) = await SetupAsync();

        // Upload a document with enough text to produce chunks
        var text = string.Join("\n\n", Enumerable.Range(1, 20).Select(i =>
            $"Section {i}: This is a paragraph about topic number {i}. " +
            $"It contains relevant information that should be indexed and made searchable. " +
            $"The content covers various aspects of the subject matter in detail."));

        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        formData.Add(fileContent, "file", "test-document.txt");

        var uploadResponse = await client.PostAsync($"/api/bots/{botId}/documents", formData);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var doc = await uploadResponse.Content.ReadFromJsonAsync<KbDocumentResponse>();
        Assert.Equal("Pending", doc!.Status);

        // Run the indexing job directly (Hangfire is in-memory, jobs don't auto-execute in tests)
        await RunIndexJobAsync(doc.Id, "1");

        // Verify document status changed to Indexed
        var getResponse = await client.GetFromJsonAsync<KbDocumentResponse>($"/api/bots/{botId}/documents/{doc.Id}");
        Assert.Equal("Indexed", getResponse!.Status);
        Assert.NotNull(getResponse.IndexedAt);

        // Verify chunks were created in the database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var chunks = await db.KbChunks
            .IgnoreQueryFilters()
            .Where(c => c.KbDocumentId == doc.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.False(string.IsNullOrEmpty(chunk.Text));
            Assert.StartsWith("char:", chunk.SourceLocation);
            Assert.NotEqual(default, chunk.Embedding);
        });

        // Verify chunk indices are sequential
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public async Task Reindex_ReplacesOldChunks()
    {
        var (client, botId) = await SetupAsync();

        // Upload and index
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Original content for reindex test. ".PadRight(500, 'x')));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        formData.Add(fileContent, "file", "reindex-test.txt");

        var uploadResponse = await client.PostAsync($"/api/bots/{botId}/documents", formData);
        var doc = await uploadResponse.Content.ReadFromJsonAsync<KbDocumentResponse>();
        await RunIndexJobAsync(doc!.Id, "1");

        // Get original chunk IDs
        using var scope1 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var originalChunkIds = await db1.KbChunks
            .IgnoreQueryFilters()
            .Where(c => c.KbDocumentId == doc.Id)
            .Select(c => c.Id)
            .ToListAsync();
        Assert.NotEmpty(originalChunkIds);

        // Reindex via API
        var reindexResponse = await client.PostAsync($"/api/bots/{botId}/documents/{doc.Id}/reindex", null);
        Assert.Equal(HttpStatusCode.Accepted, reindexResponse.StatusCode);

        // Run the job again
        await RunIndexJobAsync(doc.Id, "1");

        // Verify old chunks were replaced
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var newChunks = await db2.KbChunks
            .IgnoreQueryFilters()
            .Where(c => c.KbDocumentId == doc.Id)
            .ToListAsync();

        Assert.NotEmpty(newChunks);
        // Old chunk IDs should not exist anymore
        Assert.All(newChunks, chunk => Assert.DoesNotContain(chunk.Id, originalChunkIds));

        // Document should be Indexed again
        var getResponse = await client.GetFromJsonAsync<KbDocumentResponse>($"/api/bots/{botId}/documents/{doc.Id}");
        Assert.Equal("Indexed", getResponse!.Status);
    }

    [Fact]
    public async Task UrlIngestThenIndex_CreatesChunks()
    {
        var (client, botId) = await SetupAsync();

        var createResponse = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[] { "https://example.com/kb-article" }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var docs = await createResponse.Content.ReadFromJsonAsync<CreateKbDocumentsFromUrlsResponse>();
        var doc = Assert.Single(docs!.Created);
        Assert.Equal("Url", doc.SourceType);

        await RunIngestJobAsync(doc.Id, "1");
        await RunIndexJobAsync(doc.Id, "1");

        var getResponse = await client.GetFromJsonAsync<KbDocumentResponse>($"/api/bots/{botId}/documents/{doc.Id}");
        Assert.Equal("Indexed", getResponse!.Status);
        Assert.NotNull(getResponse.IndexedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var chunks = await db.KbChunks.IgnoreQueryFilters().Where(c => c.KbDocumentId == doc.Id).ToListAsync();
        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Text.Contains("knowledge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UrlIngestFailure_AfterIndexed_KeepsChunks()
    {
        var (client, botId) = await SetupAsync();

        var createResponse = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[] { "https://example.com/kb-article" }
        });
        var docs = await createResponse.Content.ReadFromJsonAsync<CreateKbDocumentsFromUrlsResponse>();
        var doc = Assert.Single(docs!.Created);

        await RunIngestJobAsync(doc.Id, "1");
        await RunIndexJobAsync(doc.Id, "1");

        using (var scope = _factory.Services.CreateScope())
        {
            var fetcher = (FakeUrlContentFetcher)scope.ServiceProvider.GetRequiredService<IUrlContentFetcher>();
            fetcher.FailNext = true;
        }

        var reindexResponse = await client.PostAsync($"/api/bots/{botId}/documents/{doc.Id}/reindex", null);
        Assert.Equal(HttpStatusCode.Accepted, reindexResponse.StatusCode);
        await RunIngestJobAsync(doc.Id, "1");

        var getResponse = await client.GetFromJsonAsync<KbDocumentResponse>($"/api/bots/{botId}/documents/{doc.Id}");
        Assert.Equal("Indexed", getResponse!.Status);

        using var scope2 = _factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var chunks = await db.KbChunks.IgnoreQueryFilters().Where(c => c.KbDocumentId == doc.Id).ToListAsync();
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task UrlIngestFailure_FirstAttempt_StoresErrorMessage()
    {
        var (client, botId) = await SetupAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var fetcher = (FakeUrlContentFetcher)scope.ServiceProvider.GetRequiredService<IUrlContentFetcher>();
            fetcher.FailNext = true;
        }

        var createResponse = await client.PostAsJsonAsync($"/api/bots/{botId}/documents/from-urls", new
        {
            urls = new[] { "https://example.com/blocked-page" }
        });
        var docs = await createResponse.Content.ReadFromJsonAsync<CreateKbDocumentsFromUrlsResponse>();
        var doc = Assert.Single(docs!.Created);

        await RunIngestJobAsync(doc.Id, "1");

        var getResponse = await client.GetFromJsonAsync<KbDocumentResponse>($"/api/bots/{botId}/documents/{doc.Id}");
        Assert.Equal("Failed", getResponse!.Status);
        Assert.False(string.IsNullOrWhiteSpace(getResponse.ErrorMessage));
        Assert.Contains("forced failure", getResponse.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
