using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public interface IConversationGreetingService
{
    Task PersistGreetingIfConfiguredAsync(Bot bot, int conversationId, CancellationToken cancellationToken = default);
}

public sealed class ConversationGreetingService : IConversationGreetingService
{
    private readonly BotFactoryDbContext _db;
    private readonly IConversationStreamPublisher _streamPublisher;

    public ConversationGreetingService(
        BotFactoryDbContext db,
        IConversationStreamPublisher streamPublisher)
    {
        _db = db;
        _streamPublisher = streamPublisher;
    }

    public async Task PersistGreetingIfConfiguredAsync(
        Bot bot,
        int conversationId,
        CancellationToken cancellationToken = default)
    {
        var greeting = bot.GreetingMessage?.Trim();
        if (string.IsNullOrEmpty(greeting))
            return;

        var message = new Message
        {
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = greeting,
            Provider = "greeting",
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
        await _streamPublisher.PublishMessageAsync(message, cancellationToken);
    }
}
