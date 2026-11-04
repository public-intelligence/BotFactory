using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class BotService : IBotService
{
    private readonly BotFactoryDbContext _db;

    public BotService(BotFactoryDbContext db)
    {
        _db = db;
    }

    public async Task<(List<Bot> Items, int TotalCount)> GetAllAsync(
        int page = 1,
        int pageSize = 10,
        bool publishedOnly = false)
    {
        IQueryable<Bot> query = _db.Bots;
        if (publishedOnly)
            query = query.Where(b => b.PublishedState == BotStates.Published);

        query = query.OrderByDescending(b => b.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }

    public async Task<Bot?> GetByIdAsync(int id, bool publishedOnly = false)
    {
        var bot = await _db.Bots.FindAsync(id);
        if (bot is null) return null;
        if (publishedOnly && bot.PublishedState != BotStates.Published) return null;
        return bot;
    }

    public async Task<Bot?> GetBySlugAsync(string slug, bool publishedOnly = false)
    {
        // The slug-based lookup is the entry point for the public chat URL
        // (`/chat/:slug`) and must work even for anonymous callers, so we bypass
        // the multi-tenant query filter here. Slugs are effectively a public
        // identifier and the `publishedOnly` flag protects drafts from leaking.
        // Also matches PublicId so the same /chat/:key route serves both the
        // per-org human slug and the opaque global public id.
        IQueryable<Bot> query = _db.Bots
            .IgnoreQueryFilters()
            .Where(b => b.UrlSlug == slug || (b.PublicId == slug && b.PublicId != ""));

        if (publishedOnly)
            query = query.Where(b => b.PublishedState == BotStates.Published);

        return await query.FirstOrDefaultAsync();
    }

    public async Task<Bot?> GetByPublicIdAsync(string publicId, bool publishedOnly = false)
    {
        if (string.IsNullOrWhiteSpace(publicId)) return null;

        IQueryable<Bot> query = _db.Bots
            .IgnoreQueryFilters()
            .Where(b => b.PublicId == publicId);

        if (publishedOnly)
            query = query.Where(b => b.PublishedState == BotStates.Published);

        return await query.FirstOrDefaultAsync();
    }

    public async Task<Bot> CreateAsync(Bot bot)
    {
        if (string.IsNullOrEmpty(bot.PublicId))
            bot.PublicId = PublicIdGenerator.NewId();

        _db.Bots.Add(bot);
        await _db.SaveChangesAsync();
        return bot;
    }

    public async Task<Bot?> UpdateAsync(int id, Bot updated)
    {
        var bot = await _db.Bots.FindAsync(id);
        if (bot is null) return null;

        bot.BotName = updated.BotName;
        bot.UrlSlug = updated.UrlSlug;
        bot.PublishedState = updated.PublishedState;
        bot.MasterPrompt = updated.MasterPrompt;
        bot.TopK = updated.TopK;
        bot.SilenceEndMs = updated.SilenceEndMs;
        bot.NoSpeechTimeoutMs = updated.NoSpeechTimeoutMs;
        bot.MaxUtteranceMs = updated.MaxUtteranceMs;
        bot.EnableBargeIn = updated.EnableBargeIn;
        bot.EnableTts = updated.EnableTts;
        bot.EnableTypingSound = updated.EnableTypingSound;
        bot.EnableChat = updated.EnableChat;
        bot.EnableVoice = updated.EnableVoice;
        bot.GroundedOnlyMessage = updated.GroundedOnlyMessage;
        bot.SimilarityThreshold = updated.SimilarityThreshold;
        bot.RequireGrounding = updated.RequireGrounding;
        bot.VoiceId = updated.VoiceId;
        bot.VadActivationThreshold = updated.VadActivationThreshold;
        bot.VadMinSpeechDurationMs = updated.VadMinSpeechDurationMs;
        bot.InterruptionMinDurationMs = updated.InterruptionMinDurationMs;
        bot.InterruptionMinWords = updated.InterruptionMinWords;
        bot.GreetingMessage = updated.GreetingMessage;
        bot.RequiresAuthentication = updated.RequiresAuthentication;
        bot.EmbedEnabled = updated.EmbedEnabled;
        bot.AllowedEmbedOrigins = updated.AllowedEmbedOrigins;
        bot.PublicRequestsPerMinute = updated.PublicRequestsPerMinute;
        // PublicId deliberately not copied: stable server-generated key.

        await _db.SaveChangesAsync();
        return bot;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var bot = await _db.Bots.FindAsync(id);
        if (bot is null) return false;

        _db.Bots.Remove(bot);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<Bot?> SetPublishedStateAsync(int id, string publishedState)
    {
        if (!BotStates.IsValid(publishedState))
            throw new ArgumentException($"Invalid published state: {publishedState}", nameof(publishedState));

        var bot = await _db.Bots.FindAsync(id);
        if (bot is null) return null;

        if (bot.PublishedState == publishedState) return bot;

        bot.PublishedState = publishedState;
        await _db.SaveChangesAsync();
        return bot;
    }
}
