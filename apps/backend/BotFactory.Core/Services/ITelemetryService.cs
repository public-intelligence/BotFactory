using System.Text.Json;
using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface ITelemetryService
{
    Task EmitAsync(TelemetryEmit emit, CancellationToken cancellationToken = default);

    Task<(List<TelemetryEvent> Items, int TotalCount)> GetEventsAsync(
        int? conversationId = null,
        int? botId = null,
        string[]? eventNames = null,
        EventSource? source = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<List<TelemetryEvent>> GetFeedEventsForConversationAsync(
        int conversationId,
        CancellationToken cancellationToken = default);
}

public sealed record TelemetryEmit(
    string EventName,
    EventSource Source,
    int? BotId = null,
    int? ConversationId = null,
    string? UserId = null,
    Guid? TurnId = null,
    DateTime? OccurredAt = null,
    string? Stage = null,
    int? LatencyMs = null,
    string? Provider = null,
    string? ErrorMessage = null,
    string? Summary = null,
    JsonElement? Data = null);
