using System.Text.Json;
using BotFactory.Core.Entities;

namespace BotFactory.Core.Tools;

/// <summary>
/// The controlled pipeline every configured (DB-defined) tool call goes
/// through: argument schema validation → deterministic policy evaluation →
/// credential resolution → protocol connector → response mapping/redaction →
/// ToolExecution record. Built-in tools (kb_search) bypass this entirely and
/// keep their direct IToolHandler path.
/// </summary>
public interface IToolExecutionGateway
{
    /// <summary>
    /// Conversation-path execution. Returns a ToolInvocation whose
    /// ResponsePayload is either the mapped external response or a structured
    /// error object the model can relay ("service unavailable") — it never
    /// throws for external failures.
    /// </summary>
    Task<ToolInvocation> ExecuteAsync(
        ResolvedBotTool tool,
        JsonElement args,
        ToolContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Admin test-console execution: no conversation, Draft status allowed,
    /// records the execution with IsTest = true and returns the full redacted
    /// diagnostics for display.
    /// </summary>
    Task<ToolTestResult> ExecuteTestAsync(
        ToolDefinition definition,
        ToolConnection connection,
        JsonElement args,
        CancellationToken cancellationToken);
}

public record ToolTestResult(
    ToolExecutionOutcome Outcome,
    int? HttpStatus,
    int LatencyMs,
    string ResolvedRequestSummary,
    string? RedactedResponseJson,
    string? ErrorCategory,
    string? ErrorMessage,
    IReadOnlyList<string> ValidationErrors);
