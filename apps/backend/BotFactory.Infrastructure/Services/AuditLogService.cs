using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly BotFactoryDbContext _db;

    public AuditLogService(BotFactoryDbContext db)
    {
        _db = db;
    }

    public async Task<(List<AuditLog> Items, int TotalCount)> GetLogsAsync(
        string? entityType = null,
        int? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (entityType is not null)
            query = query.Where(a => a.EntityType == entityType);
        if (entityId is not null)
            query = query.Where(a => a.EntityId == entityId);
        if (from is not null)
            query = query.Where(a => a.CreatedAt >= from);
        if (to is not null)
            query = query.Where(a => a.CreatedAt <= to);

        query = query.OrderByDescending(a => a.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return (items, totalCount);
    }
}
