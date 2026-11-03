using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class BotThinkingSoundService : IBotThinkingSoundService
{
    private readonly BotFactoryDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly ITenantContext _tenantContext;
    private readonly IBotService _botService;

    public BotThinkingSoundService(
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

    public async Task<Bot?> UploadAsync(int botId, string filename, Stream content, CancellationToken ct = default)
    {
        var bot = await _botService.GetByIdAsync(botId);
        if (bot is null) return null;

        if (bot.ThinkingSoundBlobKey is not null)
        {
            try
            {
                await _blobStorage.DeleteAsync(bot.ThinkingSoundBlobKey, ct);
            }
            catch
            {
                // Best-effort cleanup before replace
            }
        }

        var blobKey = $"{_tenantContext.OrganizationId}/{botId}/thinking-sound/{Guid.NewGuid()}/{filename}";
        await _blobStorage.UploadAsync(blobKey, content, "audio/mpeg", ct);

        bot.ThinkingSoundBlobKey = blobKey;
        bot.ThinkingSoundFilename = filename;
        await _db.SaveChangesAsync(ct);
        return bot;
    }

    public async Task<bool> DeleteAsync(int botId, CancellationToken ct = default)
    {
        var bot = await _botService.GetByIdAsync(botId);
        if (bot is null || bot.ThinkingSoundBlobKey is null) return false;

        try
        {
            await _blobStorage.DeleteAsync(bot.ThinkingSoundBlobKey, ct);
        }
        catch
        {
            // Accept orphaned blobs on partial failure
        }

        bot.ThinkingSoundBlobKey = null;
        bot.ThinkingSoundFilename = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BlobDownload?> OpenReadAsync(int botId, CancellationToken ct = default)
    {
        var bot = await _botService.GetByIdAsync(botId);
        if (bot?.ThinkingSoundBlobKey is null) return null;
        return await _blobStorage.OpenReadAsync(bot.ThinkingSoundBlobKey, ct);
    }

    public async Task<BlobDownload?> OpenReadBySlugAsync(
        string slug,
        bool publishedOnly,
        CancellationToken ct = default)
    {
        var bot = await _botService.GetBySlugAsync(slug, publishedOnly);
        if (bot?.ThinkingSoundBlobKey is null) return null;
        return await _blobStorage.OpenReadAsync(bot.ThinkingSoundBlobKey, ct);
    }
}
