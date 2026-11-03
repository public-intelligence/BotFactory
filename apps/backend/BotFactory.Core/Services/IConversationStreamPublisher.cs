using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IConversationStreamPublisher
{
    Task PublishMessageAsync(Message message, CancellationToken cancellationToken = default);

    Task PublishEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
}
