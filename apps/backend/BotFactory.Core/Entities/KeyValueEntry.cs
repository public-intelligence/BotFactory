namespace BotFactory.Core.Entities;

public class KeyValueEntry : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}
