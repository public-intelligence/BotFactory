using System.Collections.Concurrent;
using BotFactory.Core.Services;

namespace BotFactory.Infrastructure.Services;

public sealed class ChatTurnRegistry : IChatTurnRegistry
{
    private sealed record Entry(string ConnectionId, string UserId, CancellationTokenSource Cts);

    private readonly ConcurrentDictionary<Guid, Entry> _active = new();

    // One turn at a time per connection. Hub invocations of a connection may run in
    // parallel (so CancelTurn is not stuck behind the SendTurn it must cancel); this
    // keeps a second SendTurn from starting a second turn over the first.
    private readonly ConcurrentDictionary<string, Guid> _byConnection = new(StringComparer.Ordinal);

    public bool TryRegister(Guid turnId, string connectionId, string userId, CancellationTokenSource cts)
    {
        if (!_byConnection.TryAdd(connectionId, turnId))
            return false;

        if (_active.TryAdd(turnId, new Entry(connectionId, userId, cts)))
            return true;

        _byConnection.TryRemove(new KeyValuePair<string, Guid>(connectionId, turnId));
        return false;
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

    public void Unregister(Guid turnId)
    {
        if (_active.TryRemove(turnId, out var entry))
            _byConnection.TryRemove(new KeyValuePair<string, Guid>(entry.ConnectionId, turnId));
    }
}
