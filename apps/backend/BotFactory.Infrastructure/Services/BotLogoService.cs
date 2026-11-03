using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;

namespace BotFactory.Infrastructure.Services;

public class BotLogoService : IBotLogoService
{
    private readonly BotFactoryDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly ITenantContext _tenantContext;
    private readonly IBotService _botService;

    public BotLogoService(
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

    public async Task<Bot?> UploadAsync(
        int botId,
        string filename,
        string contentType,
        Stream content,
        CancellationToken ct = default)
    {
        var bot = await _botService.GetByIdAsync(botId);
        if (bot is null) return null;

        if (bot.LogoBlobKey is not null)
        {
            try
            {
                await _blobStorage.DeleteAsync(bot.LogoBlobKey, ct);
            }
            catch
            {
                // Best-effort cleanup before replace
            }
        }

        var safeName = Path.GetFileName(filename);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "logo";

        var blobKey = $"{_tenantContext.OrganizationId}/{botId}/branding/logo/{Guid.NewGuid()}/{safeName}";
        await _blobStorage.UploadAsync(blobKey, content, contentType, ct);

        bot.LogoBlobKey = blobKey;
        bot.LogoFilename = safeName;
        await _db.SaveChangesAsync(ct);
        return bot;
    }

    public async Task<bool> DeleteAsync(int botId, CancellationToken ct = default)
    {
        var bot = await _botService.GetByIdAsync(botId);
        if (bot is null || bot.LogoBlobKey is null) return false;

        try
        {
            await _blobStorage.DeleteAsync(bot.LogoBlobKey, ct);
        }
        catch
        {
            // Accept orphaned blobs on partial failure
        }

        bot.LogoBlobKey = null;
        bot.LogoFilename = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BlobDownload?> OpenReadAsync(int botId, CancellationToken ct = default)
    {
        var bot = await _botService.GetByIdAsync(botId);
        if (bot?.LogoBlobKey is null) return null;
        return await _blobStorage.OpenReadAsync(bot.LogoBlobKey, ct);
    }

    public async Task<BlobDownload?> OpenReadBySlugAsync(
        string slug,
        bool publishedOnly,
        CancellationToken ct = default)
    {
        var bot = await _botService.GetBySlugAsync(slug, publishedOnly);
        if (bot?.LogoBlobKey is null) return null;
        return await _blobStorage.OpenReadAsync(bot.LogoBlobKey, ct);
    }
}
