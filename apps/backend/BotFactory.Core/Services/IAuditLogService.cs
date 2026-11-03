using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IAuditLogService
{
    Task<(List<AuditLog> Items, int TotalCount)> GetLogsAsync(
        string? entityType = null,
        int? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20);
}
