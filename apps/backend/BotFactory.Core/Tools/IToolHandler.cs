using System.Text.Json;
using BotFactory.Core.Entities;

namespace BotFactory.Core.Tools;

public record ToolContext(Bot Bot, Conversation Conversation, Guid? TurnId = null);

public record ToolTelemetry(string EventName, string? Stage, object Data);

/// <summary>
/// One item to render in the tool-invocation feed entry. Storage (chunk ids,
/// distances, embeddings) is kept separate; this is purely how the UI shows it.
/// </summary>
public record ToolDisplayItem(string Id, string Label, string Body);

public record ToolInvocation(
    object ResponsePayload,
    ToolTelemetry? Telemetry,
    bool NoUsableResults,
    IReadOnlyList<ToolDisplayItem>? Display = null,
    string? Summary = null);

public interface IToolHandler
{
    /// <summary>
    /// Tool identifier — used both as the Gemini function name and as the UI
    /// label (the feed prepends "Tool call: …"). Pick a short slug like
    /// <c>kb_search</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// JSON-Schema function declaration sent to the model (name, description, parameters).
    /// </summary>
    FunctionDeclaration Declaration { get; }

    Task<ToolInvocation> ExecuteAsync(
        JsonElement args,
        ToolContext context,
        CancellationToken cancellationToken);
}
