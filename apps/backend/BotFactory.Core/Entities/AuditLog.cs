namespace BotFactory.Core.Entities;

public class AuditLog : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public List<AuditFieldChange> Changes { get; set; } = [];
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}
