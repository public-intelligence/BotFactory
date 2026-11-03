namespace BotFactory.Core.DTOs;

public record TelemetryEventResponse(
    int Id,
    string EventName,
    string Source,
    int? BotId,
    int? ConversationId,
    string? UserId,
    DateTime OccurredAt,
    string? Stage,
    int? LatencyMs,
    string? Provider,
    string? ErrorMessage,
    string? Summary,
    string? Data,
    Guid? TurnId = null);

public enum ConversationItemKind
{
    Message = 0,
    Event = 1,
}

public record ConversationItemDto(
    string Kind,
    DateTime OccurredAt,
    MessageItemPayload? Message,
    EventItemPayload? Event);

public record MessageItemPayload(
    int Id,
    string Role,
    string Content,
    string? Provider,
    int? LatencyMs);

public record EventItemPayload(
    int Id,
    string EventName,
    string? Stage,
    int? LatencyMs,
    string? Provider,
    string? ErrorMessage,
    string? Summary,
    string? Payload,
    Guid? TurnId = null);

// ---- Public-usage dashboard (GET /api/telemetry/public-usage) ----

public record PublicUsageOriginCount(string Origin, int Count);

public record PublicUsageDaily(
    DateTime Date,
    int PublicConversations,
    int AuthenticatedConversations,
    int RejectedSessions,
    int RateLimitEvents);

public record PublicUsageResponse(
    DateTime From,
    DateTime To,
    int AuthenticatedConversations,
    int PublicConversations,
    int DirectPublicConversations,
    int EmbeddedPublicConversations,
    List<PublicUsageOriginCount> TopEmbedOrigins,
    int RejectedSessions,
    int RateLimitEvents,
    List<PublicUsageDaily> Daily);
