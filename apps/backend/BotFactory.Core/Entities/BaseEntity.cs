namespace BotFactory.Core.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public string OrganizationId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
