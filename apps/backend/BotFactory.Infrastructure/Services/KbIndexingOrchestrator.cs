using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services;

public class KbIndexingOrchestrator
{
    private readonly BotFactoryDbContext _db;
    private readonly IAppSettingService _appSettings;
    private readonly IBackgroundJobService _jobService;
    private readonly ILogger<KbIndexingOrchestrator> _logger;

    public KbIndexingOrchestrator(
        BotFactoryDbContext db,
        IAppSettingService appSettings,
        IBackgroundJobService jobService,
        ILogger<KbIndexingOrchestrator> logger)
    {
        _db = db;
        _appSettings = appSettings;
        _jobService = jobService;
        _logger = logger;
    }

    public async Task<bool> CanIndexAsync()
    {
        var key = await _appSettings.GetDecryptedKeyAsync(ServiceType.Embeddings);
        return !string.IsNullOrEmpty(key);
    }

    /// <summary>Enqueue indexing for a file document (blob already present).</summary>
    public async Task TryEnqueueAsync(int documentId, string organizationId)
        => await TryEnqueueIndexAsync(documentId, organizationId);

    public async Task TryEnqueueIndexAsync(int documentId, string organizationId)
    {
        if (await CanIndexAsync())
        {
            _jobService.Enqueue<IndexDocumentJob>(j => j.ExecuteAsync(documentId, organizationId));
            _logger.LogInformation("Enqueued indexing for document {DocumentId}", documentId);
        }
        else
        {
            _logger.LogInformation("Document {DocumentId} left as Pending — embeddings API key not configured", documentId);
        }
    }

    public Task TryEnqueueIngestAsync(int documentId, string organizationId)
    {
        _jobService.Enqueue<IngestUrlJob>(j => j.ExecuteAsync(documentId, organizationId));
        _logger.LogInformation("Enqueued URL ingest for document {DocumentId}", documentId);
        return Task.CompletedTask;
    }

    public async Task TryEnqueueForDocumentAsync(KbDocument doc, string organizationId)
    {
        if (doc.SourceType == KbDocumentSourceType.Url)
            await TryEnqueueIngestAsync(doc.Id, organizationId);
        else
            await TryEnqueueIndexAsync(doc.Id, organizationId);
    }

    public async Task EnqueuePendingDocumentsAsync(string organizationId)
    {
        var pendingDocs = await _db.KbDocuments
            .Where(d => d.Status == DocumentStatus.Pending)
            .Select(d => new { d.Id, d.SourceType })
            .ToListAsync();

        if (pendingDocs.Count == 0) return;

        var canIndex = await CanIndexAsync();
        var enqueued = 0;

        foreach (var doc in pendingDocs)
        {
            if (doc.SourceType == KbDocumentSourceType.Url)
            {
                _jobService.Enqueue<IngestUrlJob>(j => j.ExecuteAsync(doc.Id, organizationId));
                enqueued++;
            }
            else if (canIndex)
            {
                _jobService.Enqueue<IndexDocumentJob>(j => j.ExecuteAsync(doc.Id, organizationId));
                enqueued++;
            }
        }

        _logger.LogInformation("Enqueued {Count} pending documents for ingest/index", enqueued);
    }
}
