namespace BotFactory.Core.DTOs;

/// <summary>
/// One row of the bot's Herramientas tab: every published tool of the org with
/// its association state for this bot. Built-in tools (kb_search) are not
/// listed — they are always active and not configurable.
/// </summary>
public record BotToolResponse(
    int ToolDefinitionId,
    string Name,
    string DisplayName,
    string? Category,
    string Status,
    string DataClassification,
    bool RequiresAuthenticatedUser,
    bool Enabled,
    string? AdditionalInstructions,
    int? MaxCallsPerConversationOverride);

public record BotToolUpsert(
    int ToolDefinitionId,
    bool Enabled,
    string? AdditionalInstructions,
    int? MaxCallsPerConversationOverride);

public record UpdateBotToolsRequest(List<BotToolUpsert> Tools);
