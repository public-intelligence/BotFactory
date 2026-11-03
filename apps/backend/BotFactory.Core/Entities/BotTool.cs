namespace BotFactory.Core.Entities;

/// <summary>
/// Explicit bot ↔ tool allowlist entry. A bot only sees configured tools it
/// has an enabled association with. Built-in tools (kb_search) have no rows
/// here — they are always present and cannot be toggled.
/// </summary>
public class BotTool : BaseEntity, IAuditable
{
    public int BotId { get; set; }
    public Bot? Bot { get; set; }

    public int ToolDefinitionId { get; set; }
    public ToolDefinition? ToolDefinition { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Optional per-bot AI note appended to the system instruction alongside the tool's usage notes.</summary>
    public string? AdditionalInstructions { get; set; }

    public int? MaxCallsPerConversationOverride { get; set; }
}
