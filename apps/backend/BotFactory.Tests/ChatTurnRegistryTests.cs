using BotFactory.Infrastructure.Services;

namespace BotFactory.Tests;

public class ChatTurnRegistryTests
{
    [Fact]
    public void TryRegister_Duplicate_ReturnsFalse()
    {
        var registry = new ChatTurnRegistry();
        var turnId = Guid.NewGuid();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        Assert.True(registry.TryRegister(turnId, "conn-a", "user-a", cts1));
        Assert.False(registry.TryRegister(turnId, "conn-b", "user-b", cts2));
    }

    [Fact]
    public void TryRegister_SecondTurnOnTheSameConnection_IsRefused_UntilTheFirstEnds()
    {
        var registry = new ChatTurnRegistry();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.True(registry.TryRegister(first, "conn-a", "user-a", cts1));
        Assert.False(registry.TryRegister(second, "conn-a", "user-a", cts2));
        // Another connection is independent.
        Assert.True(registry.TryRegister(Guid.NewGuid(), "conn-b", "user-b", cts2));

        registry.Unregister(first);
        Assert.True(registry.TryRegister(second, "conn-a", "user-a", cts2));
    }

    [Fact]
    public void TryCancel_MatchingConnection_Cancels()
    {
        var registry = new ChatTurnRegistry();
        var turnId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        Assert.True(registry.TryRegister(turnId, "conn-a", "user-a", cts));

        Assert.True(registry.TryCancel(turnId, "conn-a", "other-user"));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void TryCancel_MatchingUser_Cancels()
    {
        var registry = new ChatTurnRegistry();
        var turnId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        Assert.True(registry.TryRegister(turnId, "conn-a", "user-a", cts));

        Assert.True(registry.TryCancel(turnId, "other-conn", "user-a"));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void TryCancel_WrongOwner_ReturnsFalse()
    {
        var registry = new ChatTurnRegistry();
        var turnId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        Assert.True(registry.TryRegister(turnId, "conn-a", "user-a", cts));

        Assert.False(registry.TryCancel(turnId, "conn-b", "user-b"));
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void Unregister_ThenCancel_ReturnsFalse()
    {
        var registry = new ChatTurnRegistry();
        var turnId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        Assert.True(registry.TryRegister(turnId, "conn-a", "user-a", cts));
        registry.Unregister(turnId);

        Assert.False(registry.TryCancel(turnId, "conn-a", "user-a"));
    }
}
