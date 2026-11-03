namespace BotFactory.Core.Services;

public interface IChatTurnRegistry
{
    bool TryRegister(Guid turnId, string connectionId, string userId, CancellationTokenSource cts);

    bool TryCancel(Guid turnId, string connectionId, string userId);

    void Unregister(Guid turnId);
}
