using System.Collections.Concurrent;
using BotFactory.Core.Services;

namespace BotFactory.Infrastructure.Services;

public sealed class ChatTurnRegistry : IChatTurnRegistry
{
    private sealed record Entry(string ConnectionId, string UserId, CancellationTokenSource Cts);

    private readonly ConcurrentDictionary<Guid, Entry> _active = new();

    public bool TryRegister(Guid turnId, string connectionId, string userId, CancellationTokenSource cts)
    {
        var entry = new Entry(connectionId, userId, cts);
        return _active.TryAdd(turnId, entry);
    }

    public bool TryCancel(Guid turnId, string connectionId, string userId)
    {
        if (!_active.TryGetValue(turnId, out var entry))
            return false;

        if (!string.Equals(entry.ConnectionId, connectionId, StringComparison.Ordinal)
            && !string.Equals(entry.UserId, userId, StringComparison.Ordinal))
        {
            return false;
        }

        entry.Cts.Cancel();
        return true;
    }

    public void Unregister(Guid turnId) => _active.TryRemove(turnId, out _);
}
