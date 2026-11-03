namespace BotFactory.Core.Services;

/// <summary>
/// Fixed-window (1 minute) counter for anonymous public-session traffic,
/// partitioned by caller-chosen keys ("bot:{id}", "org:{orgId}"). Complements
/// the ASP.NET per-IP rate limiter: per-IP throttles a single abusive client,
/// while these caps bound total anonymous load per bot and per institution
/// regardless of how many IPs it comes from. In-memory (per instance) —
/// acceptable for the current single-instance API; revisit if scaled out.
/// </summary>
public interface IPublicRateLimiter
{
    /// <summary>
    /// Counts one request against the key's current window. Returns false when
    /// the window already reached <paramref name="permitLimit"/>. A limit of 0
    /// or less means unlimited.
    /// </summary>
    bool TryAcquire(string partitionKey, int permitLimit);
}
