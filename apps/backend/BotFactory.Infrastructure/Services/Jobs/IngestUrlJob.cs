using System.Text;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services.Jobs;

public class IngestUrlJob
{
    /// <summary>Must match AutomaticRetry Attempts in Program.cs.</summary>
    public const int MaxHangfireRetries = 3;

    private readonly ITenantContextSetter _tenantSetter;
    private readonly BotFactoryDbContext _db;
    private readonly IUrlContentFetcher _fetcher;
    private readonly IBlobStorageService _blobStorage;
    private readonly KbIndexingOrchestrator _orchestrator;
    private readonly ILogger<IngestUrlJob> _logger;

    public IngestUrlJob(
        ITenantContextSetter tenantSetter,
        BotFactoryDbContext db,
        IUrlContentFetcher fetcher,
        IBlobStorageService blobStorage,
        KbIndexingOrchestrator orchestrator,
        ILogger<IngestUrlJob> logger)
    {
        _tenantSetter = tenantSetter;
        _db = db;
        _fetcher = fetcher;
        _blobStorage = blobStorage;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task ExecuteAsync(int documentId, string organizationId, PerformContext? context = null)
    {
        _tenantSetter.SetTenant(organizationId);

        var doc = await _db.KbDocuments.FindAsync(documentId);
        if (doc is null)
        {
            _logger.LogWarning("Document {DocumentId} not found, skipping URL ingest", documentId);
            return;
        }

        if (doc.SourceType != KbDocumentSourceType.Url || string.IsNullOrWhiteSpace(doc.SourceUrl))
        {
            _logger.LogWarning("Document {DocumentId} is not a URL source, skipping ingest", documentId);
            return;
        }

        var hadPriorIndex = doc.IndexedAt is not null
            || await _db.KbChunks.AnyAsync(c => c.KbDocumentId == documentId);
        var previousIndexedAt = doc.IndexedAt;

        if (doc.Status != DocumentStatus.Pending)
        {
            doc.Status = DocumentStatus.Pending;
            doc.ErrorMessage = null;
            await _db.SaveChangesAsync();
        }

        try
        {
            var result = await _fetcher.FetchPlainTextAsync(doc.SourceUrl);
            if (!result.Success || string.IsNullOrWhiteSpace(result.PlainText))
            {
                var error = string.IsNullOrWhiteSpace(result.Error)
                    ? "No se pudo obtener contenido de la URL."
                    : result.Error!;

                if (result.Transient)
                {
                    await HandleTransientFailureAsync(
                        doc,
                        hadPriorIndex,
                        previousIndexedAt,
                        error,
                        context);
                    return;
                }

                _logger.LogWarning(
                    "URL ingest failed for document {DocumentId}: {Error}",
                    documentId,
                    error);
                await MarkFailedOrRestoreAsync(doc, hadPriorIndex, previousIndexedAt, error);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(result.PlainText);
            await using var stream = new MemoryStream(bytes);
            await _blobStorage.UploadAsync(doc.BlobKey, stream, "text/plain");

            doc.ErrorMessage = null;
            await _db.SaveChangesAsync();

            await _orchestrator.TryEnqueueIndexAsync(doc.Id, organizationId);
            _logger.LogInformation("URL ingest succeeded for document {DocumentId}; indexing enqueued", documentId);
        }
        catch (TransientKbIngestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "URL ingest threw for document {DocumentId}", documentId);
            await MarkFailedOrRestoreAsync(
                doc,
                hadPriorIndex,
                previousIndexedAt,
                $"Error inesperado al procesar la URL: {ex.Message}");
            throw;
        }
    }

    private async Task HandleTransientFailureAsync(
        KbDocument doc,
        bool hadPriorIndex,
        DateTime? previousIndexedAt,
        string error,
        PerformContext? context)
    {
        var retryCount = context?.GetJobParameter<int>("RetryCount") ?? 0;
        if (retryCount >= MaxHangfireRetries)
        {
            var finalMessage = "El servicio de renderizado está ocupado. Vuelva a reindexar en unos minutos.";
            _logger.LogWarning(
                "URL ingest exhausted retries for document {DocumentId}: {Error}",
                doc.Id,
                error);
            await MarkFailedOrRestoreAsync(doc, hadPriorIndex, previousIndexedAt, finalMessage);
            return;
        }

        _logger.LogWarning(
            "URL ingest transient failure for document {DocumentId} (retry {Retry}/{Max}): {Error}",
            doc.Id,
            retryCount,
            MaxHangfireRetries,
            error);
        // Leave Pending so Hangfire AutomaticRetry can pick it up again.
        throw new TransientKbIngestException(error);
    }

    private async Task MarkFailedOrRestoreAsync(
        KbDocument doc,
        bool hadPriorIndex,
        DateTime? previousIndexedAt,
        string errorMessage)
    {
        if (hadPriorIndex)
        {
            doc.Status = DocumentStatus.Indexed;
            doc.IndexedAt = previousIndexedAt ?? doc.IndexedAt ?? DateTime.UtcNow;
            // Prior chunks remain; don't overwrite LastError on Indexed for refresh misses.
        }
        else
        {
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = Truncate(errorMessage);
        }

        await _db.SaveChangesAsync();
    }

    private static string Truncate(string message, int max = 1000)
    {
        message = message.Trim();
        return message.Length <= max ? message : message[..(max - 1)] + "…";
    }
}

public sealed class TransientKbIngestException(string message) : Exception(message);
