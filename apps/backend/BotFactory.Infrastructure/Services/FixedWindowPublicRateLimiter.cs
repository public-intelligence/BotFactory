using System.Collections.Concurrent;
using BotFactory.Core.Services;

namespace BotFactory.Infrastructure.Services;

public class FixedWindowPublicRateLimiter : IPublicRateLimiter
{
    private static readonly long WindowTicks = TimeSpan.TicksPerMinute;

    private sealed class Counter
    {
        public long WindowStartTicks;
        public int Count;
    }

    // Bounded by (#bots + #orgs) with public traffic — no eviction needed.
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    public bool TryAcquire(string partitionKey, int permitLimit) =>
        TryAcquire(partitionKey, permitLimit, DateTime.UtcNow.Ticks);

    /// <summary>Overload with an explicit clock, for tests.</summary>
    public bool TryAcquire(string partitionKey, int permitLimit, long nowTicks)
    {
        if (permitLimit <= 0) return true;

        var counter = _counters.GetOrAdd(partitionKey, _ => new Counter { WindowStartTicks = nowTicks });
        lock (counter)
        {
            if (nowTicks - counter.WindowStartTicks >= WindowTicks)
            {
                counter.WindowStartTicks = nowTicks;
                counter.Count = 0;
            }

            if (counter.Count >= permitLimit)
                return false;

            counter.Count++;
            return true;
        }
    }
}
