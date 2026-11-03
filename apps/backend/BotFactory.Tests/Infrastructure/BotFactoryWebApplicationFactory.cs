using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;

namespace BotFactory.Tests.Infrastructure;

public class InMemoryBlobStorageService : IBlobStorageService
{
    private readonly Dictionary<string, (byte[] Data, string ContentType, string ETag, DateTimeOffset LastModified)> _store = new();

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _store[key] = (ms.ToArray(), contentType, $"\"{Guid.NewGuid():N}\"", DateTimeOffset.UtcNow);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var entry))
            throw new FileNotFoundException($"Blob not found: {key}");
        return Task.FromResult<Stream>(new MemoryStream(entry.Data));
    }

    public Task<BlobDownload?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var entry))
            return Task.FromResult<BlobDownload?>(null);
        return Task.FromResult<BlobDownload?>(
            new BlobDownload(
                new MemoryStream(entry.Data),
                entry.ContentType,
                entry.ETag,
                entry.LastModified,
                BlobKey.VersionOf(key)));
    }
}

public class BotFactoryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("LiveKit:Host", "http://localhost:7880");
        builder.UseSetting("LiveKit:ApiKey", "testkey");
        builder.UseSetting("LiveKit:ApiSecret", "testsecrettestsecrettestsecrettestsecret");
        builder.UseSetting("Encryption:MasterKey", "F9w3MK4b4DE4LC8pp6Gh8PUlZWt7NwwgdKh/ISv7zgM=");
        builder.UseSetting("BlobStorage:ConnectionString", "UseDevelopmentStorage=true");
        builder.UseSetting("BlobStorage:ContainerName", "test-bucket");
        // Pin public JWT signing so tokens minted in tests validate across factory restarts / nested hosts.
        builder.UseSetting("PublicAccess:SigningKey", "test-public-signing-key-at-least-32-chars!!");
        // Prefer local wwwroot for embed shell (never hit the real SPA in tests).
        builder.UseSetting("PublicAccess:SpaIndexUrl", "");


        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BotFactoryDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            // Add DbContext with Testcontainers connection string
            services.AddDbContext<BotFactoryDbContext>(options =>
                options.UseSqlServer(_sqlContainer.GetConnectionString()));

            // Replace blob storage with in-memory implementation
            var blobDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBlobStorageService));
            if (blobDescriptor is not null)
                services.Remove(blobDescriptor);
            services.AddSingleton<IBlobStorageService, InMemoryBlobStorageService>();

            // Replace Hangfire with in-memory storage and disable background server
            // so tests can run jobs explicitly via RunIndexJobAsync
            services.AddHangfire(config => config.UseMemoryStorage());
            var hangfireDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            (d.ImplementationType?.FullName?.Contains("Hangfire") == true
                             || d.ImplementationFactory?.Method.DeclaringType?.FullName?.Contains("Hangfire") == true))
                .ToList();
            foreach (var d in hangfireDescriptors)
                services.Remove(d);

            // Replace GeminiEmbeddingService with fake that returns deterministic vectors
            // AddHttpClient<T>() registers multiple descriptors; remove them all
            services.RemoveAll<GeminiEmbeddingService>();
            services.AddSingleton<GeminiEmbeddingService>(sp =>
                new FakeGeminiEmbeddingService());

            services.RemoveAll<IUrlContentFetcher>();
            services.AddSingleton<IUrlContentFetcher, FakeUrlContentFetcher>();
        });

        builder.UseEnvironment("Development");
    }

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        // Apply migrations
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}

public class FakeUrlContentFetcher : IUrlContentFetcher
{
    public bool FailNext { get; set; }

    public Task<UrlContentFetchResult> FetchPlainTextAsync(string url, CancellationToken ct = default)
    {
        if (FailNext)
        {
            FailNext = false;
            return Task.FromResult(new UrlContentFetchResult(false, null, "forced failure"));
        }

        var text = string.Join("\n\n", Enumerable.Range(1, 15).Select(i =>
            $"Knowledge paragraph {i} about public services and benefits for citizens. " +
            "This content is long enough for chunking and embedding during tests."));
        return Task.FromResult(new UrlContentFetchResult(true, text, null));
    }
}

public class FakeGeminiEmbeddingService : GeminiEmbeddingService
{
    public FakeGeminiEmbeddingService() : base(new HttpClient(), null!, null!) { }

    public override Task<float[][]> GenerateEmbeddingsAsync(List<string> texts, CancellationToken ct = default,
        string taskType = "RETRIEVAL_DOCUMENT")
    {
        var rng = new Random(42);
        var results = texts.Select(_ =>
        {
            var vec = new float[1536];
            for (int i = 0; i < vec.Length; i++)
                vec[i] = (float)(rng.NextDouble() * 2 - 1);
            return vec;
        }).ToArray();

        return Task.FromResult(results);
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<BotFactoryWebApplicationFactory>;
