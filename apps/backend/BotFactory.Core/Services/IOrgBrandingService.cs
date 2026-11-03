using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IOrgBrandingService
{
    Task<OrgBranding?> GetAsync(CancellationToken ct = default);
    Task<OrgBranding> UploadLogoAsync(string filename, Stream content, string contentType, CancellationToken ct = default);
    Task<OrgBranding> UploadFaviconAsync(string filename, Stream content, string contentType, CancellationToken ct = default);
    Task<bool> DeleteLogoAsync(CancellationToken ct = default);
    Task<bool> DeleteFaviconAsync(CancellationToken ct = default);
    Task<BlobDownload?> OpenLogoReadAsync(CancellationToken ct = default);
    Task<BlobDownload?> OpenFaviconReadAsync(CancellationToken ct = default);
    Task<BlobDownload?> OpenFaviconReadBySlugAsync(
        string slug,
        bool publishedOnly,
        CancellationToken ct = default);
}
