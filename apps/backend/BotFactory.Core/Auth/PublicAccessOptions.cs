namespace BotFactory.Core.Auth;

/// <summary>
/// Settings for anonymous public-bot sessions. Bound from the "PublicAccess"
/// configuration section (env: PublicAccess__*).
/// </summary>
public class PublicAccessOptions
{
    public const string SectionName = "PublicAccess";

    public string Issuer { get; set; } = "botfactory-public";
    public string Audience { get; set; } = "botfactory-public";

    /// <summary>
    /// HS256 signing key (>= 32 chars) for anonymous session tokens. When
    /// empty, a random per-process key is generated at startup — fine for dev,
    /// but anonymous sessions won't survive restarts or scale-out. Set
    /// PublicAccess__SigningKey in production.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Anonymous session token lifetime. Re-minted on resume.</summary>
    public int TokenTtlMinutes { get; set; } = 15;

    /// <summary>LiveKit join-token TTL for anonymous voice sessions.</summary>
    public int LiveKitTokenTtlMinutes { get; set; } = 30;

    /// <summary>Cap on simultaneously-active anonymous conversations per bot.</summary>
    public int MaxActiveAnonymousConversationsPerBot { get; set; } = 20;

    /// <summary>Fixed-window per-IP request limit for /api/public and /api/embed.</summary>
    public int RequestsPerMinutePerIp { get; set; } = 30;

    /// <summary>
    /// Default per-bot cap on anonymous session-start/resume requests per
    /// minute (across all IPs). Overridable per bot via Bot.PublicRequestsPerMinute.
    /// </summary>
    public int BotRequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Per-organization cap on anonymous session-start/resume requests per
    /// minute, across all of the org's public bots.
    /// </summary>
    public int OrgRequestsPerMinute { get; set; } = 300;

    /// <summary>
    /// Absolute URL of the SPA's index.html for /api/embed/{publicId}. Prefer
    /// https://&lt;public-host&gt;/index.html. In Docker stacks the API first
    /// loads the shell via the bf-frontend container (TLS Host/SNI = this URL's
    /// host, or the embed request host when this value is empty/http/bf-frontend),
    /// then falls back to fetching this URL directly, then wwwroot/index.html.
    /// </summary>
    public string SpaIndexUrl { get; set; } = string.Empty;
}
