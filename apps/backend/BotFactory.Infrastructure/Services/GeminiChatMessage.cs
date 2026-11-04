using System.Text.Json;
using BotFactory.Core.Entities;

namespace BotFactory.Infrastructure.Services;

/// <summary>
/// One message of the conversation history sent to the model. An assistant message may
/// carry the tool lookups that backed it, so the model sees "question → lookup → answer"
/// instead of answers that seem to come from nowhere (which it then imitates).
/// </summary>
public record GeminiChatMessage(
    MessageRole Role,
    string Content,
    IReadOnlyList<HistoryToolExchange>? ToolExchanges = null);

/// <summary>
/// A past tool call and its result. <paramref name="Result"/> is the stored JSON, or a
/// trimmed excerpt of it (then not valid JSON, and sent as text).
/// </summary>
public record HistoryToolExchange(string Name, JsonElement Args, string Result);
