using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class KeyValueStore : IKeyValueStore
{
    private readonly BotFactoryDbContext _db;

    public KeyValueStore(BotFactoryDbContext db)
    {
        _db = db;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var entry = await _db.KeyValueEntries.FirstOrDefaultAsync(k => k.Key == key, cancellationToken);
        if (entry is null) return default;
        if (entry.ExpiresAt is not null && entry.ExpiresAt <= DateTime.UtcNow) return default;
        return JsonSerializer.Deserialize<T>(entry.Value);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value);
        var expiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : (DateTime?)null;

        var existing = await _db.KeyValueEntries.FirstOrDefaultAsync(k => k.Key == key, cancellationToken);
        if (existing is not null)
        {
            existing.Value = json;
            existing.ExpiresAt = expiresAt;
        }
        else
        {
            _db.KeyValueEntries.Add(new KeyValueEntry
            {
                Key = key,
                Value = json,
                ExpiresAt = expiresAt,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = await _db.KeyValueEntries.FirstOrDefaultAsync(k => k.Key == key, cancellationToken);
        if (entry is null) return false;
        _db.KeyValueEntries.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
