using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace BotFactory.Api.Hubs;

public class ConversationStreamPublisher : IConversationStreamPublisher
{
    /// <summary>
    /// Events that carry user-visible content (e.g. KB retrieval). They route to
    /// the messages group so every conversation viewer sees them, not just admins
    /// with telemetry:read.
    /// </summary>
    public static readonly HashSet<string> ContentEvents = new(StringComparer.Ordinal)
    {
        "kb_retrieved",
    };

    private readonly IHubContext<ConversationHub> _hub;

    public ConversationStreamPublisher(IHubContext<ConversationHub> hub)
    {
        _hub = hub;
    }

    public Task PublishMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        var dto = ToItem(message);
        return _hub.Clients
            .Group(ConversationHub.MessagesGroup(message.ConversationId))
            .SendAsync("messageAdded", dto, cancellationToken);
    }

    public Task PublishEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        if (telemetryEvent.ConversationId is null)
            return Task.CompletedTask;

        var dto = ToItem(telemetryEvent);
        var group = ContentEvents.Contains(telemetryEvent.EventName)
            ? ConversationHub.MessagesGroup(telemetryEvent.ConversationId.Value)
            : ConversationHub.EventsGroup(telemetryEvent.ConversationId.Value);
        return _hub.Clients.Group(group).SendAsync("eventAdded", dto, cancellationToken);
    }

    private static ConversationItemDto ToItem(Message m)
    {
        var role = m.Role.ToString();
        var (contacts, displayContent) = ContactLine.ForMessage(role, m.Content);
        return new ConversationItemDto(
            Kind: "message",
            OccurredAt: DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc),
            Message: new MessageItemPayload(
                m.Id,
                role,
                m.Content,
                m.Provider,
                m.LatencyMs,
                contacts,
                displayContent),
            Event: null);
    }

    private static ConversationItemDto ToItem(TelemetryEvent e) => new(
        Kind: "event",
        OccurredAt: DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc),
        Message: null,
        Event: new EventItemPayload(
            e.Id,
            e.EventName,
            e.Stage,
            e.LatencyMs,
            e.Provider,
            e.ErrorMessage,
            e.Summary,
            e.Data,
            e.TurnId));
}
