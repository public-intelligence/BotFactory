using BotFactory.Core.Entities;

namespace BotFactory.Core.Tools;

/// <summary>
/// A configured tool resolved for the current bot: definition + connection +
/// the bot association that enabled it.
/// </summary>
public record ResolvedBotTool(
    ToolDefinition Definition,
    ToolConnection Connection,
    BotTool Association);

/// <summary>
/// Scoped per-turn tool context, mirroring the ITenantContext/ITenantContextSetter
/// idiom. ChatTurnService calls SetBotAsync once per turn; the ToolRegistry then
/// composes built-in handlers + these resolved tools synchronously. When no bot
/// has been set (other request paths, tests), EnabledTools is empty and the
/// registry exposes built-ins only — identical to pre-feature behavior.
/// </summary>
public interface IToolsetContext
{
    Bot? Bot { get; }
    IReadOnlyList<ResolvedBotTool> EnabledTools { get; }
}

public interface IToolsetContextSetter
{
    /// <summary>
    /// Loads the bot's enabled, published tool associations (active connections
    /// only) into the scoped context.
    /// </summary>
    Task SetBotAsync(Bot bot, CancellationToken cancellationToken);
}
