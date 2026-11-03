namespace BotFactory.Core.DTOs;

public record CloseConversationRequest(string Reason);

/// <summary>
/// Client-reported context sent when an anonymous public session starts.
/// Best-effort observability data (spoofable, never used for authorization);
/// the server strips query strings and validates the origin shape.
/// </summary>
public record PublicChatStartRequest(
    string? SourceUrl = null,
    string? ParentOrigin = null,
    bool? Embedded = null);

public record ConversationResponse(
    int Id,
    int BotId,
    string UserId,
    string Status,
    string AccessMode,
    int MessageCount,
    DateTime CreatedAt,
    DateTime? ClosedAt);

public record ConversationDetailResponse(
    int Id,
    int BotId,
    string UserId,
    string Status,
    string AccessMode,
    string Surface,
    string? SourceUrl,
    string? ParentOrigin,
    DateTime CreatedAt,
    DateTime? PausedAt,
    string? PausedReason,
    DateTime? ClosedAt,
    string? ClosedReason,
    List<MessageResponse> Messages,
    List<TelemetryEventResponse>? Events);

public record ChatSessionResponse(
    int ConversationId,
    string? Token = null,
    string? Url = null,
    string? RoomName = null);

/// <summary>
/// Response for anonymous public sessions. AccessToken is the stateless public
/// JWT the client must present (Bearer / hub access_token) on every follow-up
/// call for this conversation; Token/Url/RoomName are the LiveKit voice leg.
/// </summary>
public record PublicChatSessionResponse(
    int ConversationId,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string? Token = null,
    string? Url = null,
    string? RoomName = null);

public record MessageResponse(
    int Id,
    string Role,
    string Content,
    string? Provider,
    int? LatencyMs,
    DateTime CreatedAt);
