using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BotFactory.Infrastructure.Services;

/// <summary>
/// The contact data (phones, service codes, links, e-mails) of a bot's whole knowledge base,
/// for the gate of «Respaldo obligatorio». A turn's lookup brings a few fragments; the model
/// may still give the right phone or link of an institution that is in the base but not in
/// them (TAINA: the EDEEste, EDENORTE and EDESUR sites, conversation 2219; the Supérate and
/// Pro Consumidor phones, 2303 and 2392). Only contact data counts this way: amounts and other
/// figures must come from what the turn consulted.
/// <para>
/// Read once per version of the base (indexed documents and the last indexing time) and kept
/// in memory; TAINA's base is ~950 fragments, 2.5 million characters.
/// </para>
/// </summary>
public sealed class KbContactEvidence
{
    private static readonly TimeSpan Keep = TimeSpan.FromHours(6);

    private readonly BotFactoryDbContext _db;
    private readonly IMemoryCache _cache;

    public KbContactEvidence(BotFactoryDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<string>> GetAsync(int botId, CancellationToken cancellationToken)
    {
        var indexed = _db.KbDocuments.Where(d => d.BotId == botId && d.Status == DocumentStatus.Indexed);
        var count = await indexed.CountAsync(cancellationToken);
        if (count == 0)
            return [];
        var last = await indexed.MaxAsync(d => d.IndexedAt, cancellationToken);

        var key = $"kb-contacts:{botId}:{count}:{last:O}";
        if (_cache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var texts = await (
                from c in _db.KbChunks
                join d in indexed on c.KbDocumentId equals d.Id
                select c.Text)
            .ToListAsync(cancellationToken);

        IReadOnlyList<string> items = texts
            .SelectMany(GroundingEvidence.ContactItems)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _cache.Set(key, items, Keep);
        return items;
    }
}
