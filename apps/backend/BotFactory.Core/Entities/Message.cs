namespace BotFactory.Core.Entities;

public class Message : BaseEntity
{
    public int ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public int? LatencyMs { get; set; }
}
