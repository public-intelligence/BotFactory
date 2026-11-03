using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class OrgBrandingService : IOrgBrandingService
{
    private readonly BotFactoryDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly ITenantContext _tenantContext;
    private readonly IBotService _botService;

    public OrgBrandingService(
        BotFactoryDbContext db,
        IBlobStorageService blobStorage,
        ITenantContext tenantContext,
        IBotService botService)
    {
        _db = db;
        _blobStorage = blobStorage;
        _tenantContext = tenantContext;
        _botService = botService;
    }

    public async Task<OrgBranding?> GetAsync(CancellationToken ct = default)
        => await _db.OrgBrandings.FirstOrDefaultAsync(ct);

    public Task<OrgBranding> UploadLogoAsync(string filename, Stream content, string contentType, CancellationToken ct = default)
        => UploadAssetAsync(
            filename,
            content,
            contentType,
            "logo",
            b => b.LogoBlobKey,
            (b, key) => b.LogoBlobKey = key,
            (b, name) => b.LogoFilename = name,
            ct);

    public Task<OrgBranding> UploadFaviconAsync(string filename, Stream content, string contentType, CancellationToken ct = default)
        => UploadAssetAsync(
            filename,
            content,
            contentType,
            "favicon",
            b => b.FaviconBlobKey,
            (b, key) => b.FaviconBlobKey = key,
            (b, name) => b.FaviconFilename = name,
            ct);

    public async Task<bool> DeleteLogoAsync(CancellationToken ct = default)
        => await DeleteAssetAsync(
            b => b.LogoBlobKey,
            (b) => { b.LogoBlobKey = null; b.LogoFilename = null; },
            ct);

    public async Task<bool> DeleteFaviconAsync(CancellationToken ct = default)
        => await DeleteAssetAsync(
            b => b.FaviconBlobKey,
            (b) => { b.FaviconBlobKey = null; b.FaviconFilename = null; },
            ct);

    public async Task<BlobDownload?> OpenLogoReadAsync(CancellationToken ct = default)
    {
        var branding = await GetAsync(ct);
        if (branding?.LogoBlobKey is null) return null;
        return await _blobStorage.OpenReadAsync(branding.LogoBlobKey, ct);
    }

    public async Task<BlobDownload?> OpenFaviconReadAsync(CancellationToken ct = default)
    {
        var branding = await GetAsync(ct);
        if (branding?.FaviconBlobKey is null) return null;
        return await _blobStorage.OpenReadAsync(branding.FaviconBlobKey, ct);
    }

    public async Task<BlobDownload?> OpenFaviconReadBySlugAsync(
        string slug,
        bool publishedOnly,
        CancellationToken ct = default)
    {
        var bot = await _botService.GetBySlugAsync(slug, publishedOnly);
        if (bot is null) return null;

        var branding = await _db.OrgBrandings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.OrganizationId == bot.OrganizationId, ct);

        if (branding?.FaviconBlobKey is null) return null;
        return await _blobStorage.OpenReadAsync(branding.FaviconBlobKey, ct);
    }

    private async Task<OrgBranding> UploadAssetAsync(
        string filename,
        Stream content,
        string contentType,
        string assetFolder,
        Func<OrgBranding, string?> getBlobKey,
        Action<OrgBranding, string> setBlobKey,
        Action<OrgBranding, string> setFilename,
        CancellationToken ct)
    {
        var branding = await GetOrCreateAsync(ct);

        var priorKey = getBlobKey(branding);
        if (priorKey is not null)
        {
            try
            {
                await _blobStorage.DeleteAsync(priorKey, ct);
            }
            catch
            {
                // Best-effort cleanup before replace
            }
        }

        var safeName = Path.GetFileName(filename);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = assetFolder;

        var blobKey = $"{_tenantContext.OrganizationId}/branding/{assetFolder}/{Guid.NewGuid()}/{safeName}";
        await _blobStorage.UploadAsync(blobKey, content, contentType, ct);

        setBlobKey(branding, blobKey);
        setFilename(branding, safeName);
        await _db.SaveChangesAsync(ct);
        return branding;
    }

    private async Task<bool> DeleteAssetAsync(
        Func<OrgBranding, string?> getBlobKey,
        Action<OrgBranding> clear,
        CancellationToken ct)
    {
        var branding = await GetAsync(ct);
        if (branding is null) return false;

        var key = getBlobKey(branding);
        if (key is null) return false;

        try
        {
            await _blobStorage.DeleteAsync(key, ct);
        }
        catch
        {
            // Accept orphaned blobs on partial failure
        }

        clear(branding);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<OrgBranding> GetOrCreateAsync(CancellationToken ct)
    {
        var existing = await GetAsync(ct);
        if (existing is not null) return existing;

        var branding = new OrgBranding();
        _db.OrgBrandings.Add(branding);
        await _db.SaveChangesAsync(ct);
        return branding;
    }
}
