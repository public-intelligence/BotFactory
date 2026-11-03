using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IBotLogoService
{
    Task<Bot?> UploadAsync(int botId, string filename, string contentType, Stream content, CancellationToken ct = default);
    Task<bool> DeleteAsync(int botId, CancellationToken ct = default);
    Task<BlobDownload?> OpenReadAsync(int botId, CancellationToken ct = default);
    Task<BlobDownload?> OpenReadBySlugAsync(
        string slug,
        bool publishedOnly,
        CancellationToken ct = default);
}
