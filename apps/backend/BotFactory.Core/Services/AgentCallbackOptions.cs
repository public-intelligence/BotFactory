namespace BotFactory.Core.Services;

/// <summary>
/// Shared-secret gate for the /livekit/* agent callbacks (bound from
/// "AgentCallback"). The Python agent sends the same secret in the
/// X-Agent-Secret header (env BACKEND_SHARED_SECRET). When unset the callbacks
/// stay open (dev convenience) — required before exposing any public bot.
/// </summary>
public class AgentCallbackOptions
{
    public const string SectionName = "AgentCallback";
    public const string HeaderName = "X-Agent-Secret";

    public string SharedSecret { get; set; } = string.Empty;
}
