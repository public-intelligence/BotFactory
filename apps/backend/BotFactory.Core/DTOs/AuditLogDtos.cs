using BotFactory.Core.Entities;

namespace BotFactory.Core.DTOs;

public record AuditLogResponse(
    int Id,
    string EntityType,
    int EntityId,
    string Action,
    List<AuditFieldChange> Changes,
    string UserId,
    string UserName,
    DateTime CreatedAt);
