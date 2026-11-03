namespace BotFactory.Core.DTOs;

public record ToolExecutionResponse(
    int Id,
    int? BotId,
    int? ConversationId,
    Guid? TurnId,
    string ToolName,
    string ConnectorType,
    string Outcome,
    int? HttpStatus,
    string? ErrorCategory,
    string? ErrorMessage,
    string ResolvedRequestSummary,
    string ArgumentsJson,
    string? ResultSnippetJson,
    long? ResponseBytes,
    int LatencyMs,
    int RetryCount,
    bool IsTest,
    DateTime CreatedAt);
