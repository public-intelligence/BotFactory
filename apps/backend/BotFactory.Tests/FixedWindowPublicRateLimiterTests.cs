using BotFactory.Infrastructure.Services;

namespace BotFactory.Tests;

public class FixedWindowPublicRateLimiterTests
{
    private static readonly long T0 = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc).Ticks;

    [Fact]
    public void AllowsUpToLimitWithinWindow()
    {
        var limiter = new FixedWindowPublicRateLimiter();
        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("bot:1", 5, T0 + i));

        Assert.False(limiter.TryAcquire("bot:1", 5, T0 + 10));
    }

    [Fact]
    public void ResetsAfterWindowElapses()
    {
        var limiter = new FixedWindowPublicRateLimiter();
        Assert.True(limiter.TryAcquire("bot:1", 1, T0));
        Assert.False(limiter.TryAcquire("bot:1", 1, T0 + TimeSpan.TicksPerSecond));

        var nextWindow = T0 + TimeSpan.TicksPerMinute;
        Assert.True(limiter.TryAcquire("bot:1", 1, nextWindow));
    }

    [Fact]
    public void PartitionsAreIndependent()
    {
        var limiter = new FixedWindowPublicRateLimiter();
        Assert.True(limiter.TryAcquire("bot:1", 1, T0));
        Assert.True(limiter.TryAcquire("bot:2", 1, T0));
        Assert.True(limiter.TryAcquire("org:7", 1, T0));
        Assert.False(limiter.TryAcquire("bot:1", 1, T0 + 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveLimitMeansUnlimited(int limit)
    {
        var limiter = new FixedWindowPublicRateLimiter();
        for (var i = 0; i < 100; i++)
            Assert.True(limiter.TryAcquire("bot:1", limit, T0 + i));
    }
}
