using System.Text.Json;
using BotFactory.Core.DTOs;

namespace BotFactory.Core.Services;

/// <summary>
/// Adds up the tokens a conversation spent. Every call to the chat model records an `llm_usage`
/// telemetry event with the tokens the provider reported; the conversation's total is the sum of
/// those events, so no column of its own is kept (and nothing is lost if the model changes: the
/// event carries the same fields whichever provider made the call).
/// </summary>
public static class ConversationTokenUsage
{
    /// <summary>The telemetry event recorded for every call to the chat model.</summary>
    public const string EventName = "llm_usage";

    /// <summary>
    /// Sums the `Data` of `llm_usage` events. Null when none reported tokens — a conversation
    /// that never reached the model (a greeting only) or one that predates the recording — so the
    /// panel can show «—» instead of a misleading 0. A call the provider sent no counts for adds
    /// nothing, and neither does a payload that cannot be read.
    /// </summary>
    public static TokenUsageResponse? Sum(IEnumerable<string?> eventData)
    {
        var calls = 0;
        long prompt = 0, cached = 0, output = 0, thoughts = 0, total = 0;

        foreach (var data in eventData)
        {
            if (string.IsNullOrWhiteSpace(data))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;
                if (root.TryGetProperty("reported", out var reported) && reported.ValueKind == JsonValueKind.False)
                    continue;

                var callPrompt = Read(root, "promptTokens");
                var callCached = Read(root, "cachedTokens");
                var callOutput = Read(root, "outputTokens");
                var callThoughts = Read(root, "thoughtsTokens");
                // totalTokens is what the provider billed the call as; fall back to the parts.
                var callTotal = root.TryGetProperty("totalTokens", out var t) && t.ValueKind == JsonValueKind.Number
                    ? t.GetInt64()
                    : callPrompt + callOutput + callThoughts;

                if (callTotal == 0 && callPrompt == 0 && callOutput == 0)
                    continue;

                calls++;
                prompt += callPrompt;
                cached += callCached;
                output += callOutput;
                thoughts += callThoughts;
                total += callTotal;
            }
            catch (JsonException)
            {
                // A payload nobody can read adds nothing; the rest of the conversation still counts.
            }
        }

        return calls == 0 ? null : new TokenUsageResponse(calls, prompt, cached, output, thoughts, total);
    }

    private static long Read(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
