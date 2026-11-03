using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IKbDocumentService
{
    public const int MaxUrlsPerRequest = 5000;
    public const int MaxBulkDocuments = 5000;

    Task<(List<KbDocument> Items, int TotalCount)> GetByBotIdAsync(
        int botId,
        int page = 1,
        int pageSize = 10,
        string? sortBy = null,
        string? sortDir = null,
        string? search = null,
        string? status = null,
        string? sourceType = null);
    Task<KbDocument?> GetByIdAsync(int botId, int docId);
    Task<KbDocument> CreateAsync(int botId, string filename, Stream fileContent);
    Task<(List<KbDocument> Created, int SkippedExistingCount)> CreateFromUrlsAsync(int botId, IReadOnlyList<string> urls);
    Task<bool> DeleteAsync(int botId, int docId);
    Task<List<KbDocument>> ResolveBulkAsync(
        int botId,
        IReadOnlyList<int>? documentIds,
        string? status,
        string? sourceType,
        string? search);
    Task<int> DeleteManyAsync(int botId, IReadOnlyList<KbDocument> documents);
    Task<(BlobDownload Blob, string Filename)?> OpenReadAsync(int botId, int docId, CancellationToken ct = default);
    Task<(string Filename, string Content)?> GetContentAsync(int botId, int docId, CancellationToken ct = default);
}
