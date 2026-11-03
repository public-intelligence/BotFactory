using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class KbDocumentService : IKbDocumentService
{
    private readonly BotFactoryDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly ITenantContext _tenantContext;

    public KbDocumentService(BotFactoryDbContext db, IBlobStorageService blobStorage, ITenantContext tenantContext)
    {
        _db = db;
        _blobStorage = blobStorage;
        _tenantContext = tenantContext;
    }

    public async Task<(List<KbDocument> Items, int TotalCount)> GetByBotIdAsync(
        int botId,
        int page = 1,
        int pageSize = 10,
        string? sortBy = null,
        string? sortDir = null,
        string? search = null,
        string? status = null,
        string? sourceType = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = BuildListQuery(botId, search, status, sourceType);

        var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "filename" or "nombre" => descending
                ? query.OrderByDescending(d => d.Filename)
                : query.OrderBy(d => d.Filename),
            "sourcetype" or "tipo" => descending
                ? query.OrderByDescending(d => d.SourceType)
                : query.OrderBy(d => d.SourceType),
            "status" or "estado" => descending
                ? query.OrderByDescending(d => d.Status)
                : query.OrderBy(d => d.Status),
            "indexedat" or "indexado" => descending
                ? query.OrderByDescending(d => d.IndexedAt)
                : query.OrderBy(d => d.IndexedAt),
            "createdat" => descending
                ? query.OrderByDescending(d => d.CreatedAt)
                : query.OrderBy(d => d.CreatedAt),
            _ => query.OrderByDescending(d => d.CreatedAt),
        };

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }

    public async Task<KbDocument?> GetByIdAsync(int botId, int docId)
    {
        return await _db.KbDocuments.FirstOrDefaultAsync(d => d.BotId == botId && d.Id == docId);
    }

    public async Task<KbDocument> CreateAsync(int botId, string filename, Stream fileContent)
    {
        var blobKey = $"{_tenantContext.OrganizationId}/{botId}/{Guid.NewGuid()}/{filename}";

        var doc = new KbDocument
        {
            BotId = botId,
            BlobKey = blobKey,
            Filename = filename,
            SourceType = KbDocumentSourceType.File,
            SourceUrl = null,
            Status = DocumentStatus.Pending,
        };

        _db.KbDocuments.Add(doc);
        await _blobStorage.UploadAsync(blobKey, fileContent, "text/plain");
        await _db.SaveChangesAsync();

        return doc;
    }

    public async Task<(List<KbDocument> Created, int SkippedExistingCount)> CreateFromUrlsAsync(
        int botId,
        IReadOnlyList<string> urls)
    {
        var existing = await _db.KbDocuments
            .Where(d => d.BotId == botId && d.SourceType == KbDocumentSourceType.Url && d.SourceUrl != null)
            .Select(d => d.SourceUrl!)
            .ToListAsync();

        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var docs = new List<KbDocument>();
        var skipped = 0;

        foreach (var url in urls)
        {
            if (!existingSet.Add(url))
            {
                skipped++;
                continue;
            }

            var blobKey = $"{_tenantContext.OrganizationId}/{botId}/{Guid.NewGuid()}/content.txt";
            var doc = new KbDocument
            {
                BotId = botId,
                BlobKey = blobKey,
                Filename = url,
                SourceType = KbDocumentSourceType.Url,
                SourceUrl = url,
                Status = DocumentStatus.Pending,
            };
            _db.KbDocuments.Add(doc);
            docs.Add(doc);
        }

        if (docs.Count > 0)
            await _db.SaveChangesAsync();

        return (docs, skipped);
    }

    public async Task<bool> DeleteAsync(int botId, int docId)
    {
        var doc = await _db.KbDocuments.FirstOrDefaultAsync(d => d.BotId == botId && d.Id == docId);
        if (doc is null) return false;

        await DeleteManyAsync(botId, [doc]);
        return true;
    }

    public async Task<List<KbDocument>> ResolveBulkAsync(
        int botId,
        IReadOnlyList<int>? documentIds,
        string? status,
        string? sourceType,
        string? search)
    {
        if (documentIds is { Count: > 0 })
        {
            if (documentIds.Count > IKbDocumentService.MaxBulkDocuments)
                throw new InvalidOperationException($"A maximum of {IKbDocumentService.MaxBulkDocuments} documents is allowed per request.");

            var idSet = documentIds.Distinct().ToList();
            return await _db.KbDocuments
                .Where(d => d.BotId == botId && idSet.Contains(d.Id))
                .ToListAsync();
        }

        var query = BuildListQuery(botId, search, status, sourceType);
        var count = await query.CountAsync();
        if (count > IKbDocumentService.MaxBulkDocuments)
            throw new InvalidOperationException($"A maximum of {IKbDocumentService.MaxBulkDocuments} documents is allowed per request.");

        return await query.OrderBy(d => d.Id).ToListAsync();
    }

    public async Task<int> DeleteManyAsync(int botId, IReadOnlyList<KbDocument> documents)
    {
        if (documents.Count == 0) return 0;

        var owned = documents.Where(d => d.BotId == botId).ToList();
        foreach (var doc in owned)
        {
            try
            {
                await _blobStorage.DeleteAsync(doc.BlobKey);
            }
            catch
            {
                // Accept orphaned blobs on partial failure
            }
        }

        _db.KbDocuments.RemoveRange(owned);
        await _db.SaveChangesAsync();
        return owned.Count;
    }

    public async Task<(BlobDownload Blob, string Filename)?> OpenReadAsync(
        int botId,
        int docId,
        CancellationToken ct = default)
    {
        var doc = await _db.KbDocuments.FirstOrDefaultAsync(d => d.BotId == botId && d.Id == docId, ct);
        if (doc is null) return null;

        var opened = await _blobStorage.OpenReadAsync(doc.BlobKey, ct);
        if (opened is null) return null;
        return (opened, doc.Filename);
    }

    public async Task<(string Filename, string Content)?> GetContentAsync(int botId, int docId, CancellationToken ct = default)
    {
        var doc = await _db.KbDocuments.FirstOrDefaultAsync(d => d.BotId == botId && d.Id == docId, ct);
        if (doc is null) return null;

        await using var stream = await _blobStorage.DownloadAsync(doc.BlobKey, ct);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(ct);
        return (doc.Filename, content);
    }

    private IQueryable<KbDocument> BuildListQuery(
        int botId,
        string? search,
        string? status,
        string? sourceType)
    {
        var query = _db.KbDocuments.Where(d => d.BotId == botId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(d =>
                d.Filename.Contains(term)
                || (d.SourceUrl != null && d.SourceUrl.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<DocumentStatus>(status.Trim(), ignoreCase: true, out var statusEnum))
        {
            query = query.Where(d => d.Status == statusEnum);
        }

        if (!string.IsNullOrWhiteSpace(sourceType)
            && Enum.TryParse<KbDocumentSourceType>(sourceType.Trim(), ignoreCase: true, out var sourceEnum))
        {
            query = query.Where(d => d.SourceType == sourceEnum);
        }

        return query;
    }
}
