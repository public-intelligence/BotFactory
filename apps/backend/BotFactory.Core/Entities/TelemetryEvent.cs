namespace BotFactory.Core.Entities;

public class TelemetryEvent : BaseEntity
{
    public string EventName { get; set; } = string.Empty;
    public EventSource Source { get; set; }
    public int? BotId { get; set; }
    public int? ConversationId { get; set; }
    public string? UserId { get; set; }

    /// <summary>
    /// Correlates every event emitted during a single conversational turn
    /// (one user utterance → one agent response). Minted by the agent at
    /// end-of-utterance and threaded through the backend turn. Null for
    /// events that don't belong to a turn (e.g. conversation lifecycle).
    /// </summary>
    public Guid? TurnId { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? Stage { get; set; }
    public int? LatencyMs { get; set; }
    public string? Provider { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Optional one-line human description of the event (e.g. the LLM prompt
    /// being requested, the search query). Shown inline in the admin feed.
    /// </summary>
    public string? Summary { get; set; }

    public string? Data { get; set; }
}
