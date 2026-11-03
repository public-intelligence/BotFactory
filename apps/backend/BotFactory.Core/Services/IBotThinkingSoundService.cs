using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IBotThinkingSoundService
{
    Task<Bot?> UploadAsync(int botId, string filename, Stream content, CancellationToken ct = default);
    Task<bool> DeleteAsync(int botId, CancellationToken ct = default);
    Task<BlobDownload?> OpenReadAsync(int botId, CancellationToken ct = default);
    Task<BlobDownload?> OpenReadBySlugAsync(
        string slug,
        bool publishedOnly,
        CancellationToken ct = default);
}
