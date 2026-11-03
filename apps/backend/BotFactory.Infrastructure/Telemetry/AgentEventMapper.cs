using System.Text.Json;
using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;

namespace BotFactory.Infrastructure.Telemetry;

/// <summary>
/// Pure mapper translating LiveKit-agent <c>AgentEvent</c> payloads
/// (eventType + data blob, see PR #15 / issue #36) into the canonical
/// <see cref="TelemetryEmit"/> the backend persists. Returns null when
/// the event should be skipped (lifecycle owned by another service or
/// partial transcripts that are UI-only).
/// </summary>
public static class AgentEventMapper
{
    public static TelemetryEmit? Map(string eventType, int? botId, int? conversationId, DateTime? occurredAt, JsonElement? data)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return null;

        switch (eventType)
        {
            case "ConversationStarted":
                return new TelemetryEmit(
                    "conversation_started",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "lifecycle",
                    Data: data);

            case "Close":
                // Close lifecycle is owned by ConversationService.CloseAsync,
                // which emits conversation_ended itself. Returning null avoids
                // double-emission while the endpoint still forwards the close.
                return null;

            case "Error":
            {
                var sourceLabel = ExtractSourceLabel(data);
                var rawError = ExtractError(data);
                var operatorSummary = ExtractOperatorMessage(data);
                if (string.IsNullOrWhiteSpace(operatorSummary) && rawError is not null)
                {
                    var classified = ProviderErrorClassifier.ClassifyAgentError(sourceLabel, rawError);
                    operatorSummary = classified.OperatorMessage;
                }

                var name = sourceLabel switch
                {
                    "stt" => "stt_failed",
                    "tts" => "tts_failed",
                    "llm" => "llm_error",
                    _ => "agent_error",
                };
                return new TelemetryEmit(
                    name,
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: sourceLabel ?? "agent",
                    ErrorMessage: rawError,
                    Summary: operatorSummary,
                    Data: data);
            }

            case "MetricsCollected":
            {
                var (stage, latencyMs, provider) = ExtractMetrics(data);
                return new TelemetryEmit(
                    "metrics_collected",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: stage,
                    LatencyMs: latencyMs,
                    Provider: provider,
                    Data: data);
            }

            case "UserInputTranscribed":
            {
                // Skip partials — they are UI-only. We only persist commits.
                if (data is { } el
                    && el.ValueKind == JsonValueKind.Object
                    && el.TryGetProperty("isFinal", out var f)
                    && f.ValueKind == JsonValueKind.False)
                {
                    return null;
                }
                return new TelemetryEmit(
                    "user_input_transcribed",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "stt",
                    Data: data);
            }

            case "AgentStateChanged":
            {
                var summary = ExtractStateTransition(data);
                return new TelemetryEmit(
                    "agent_state_changed",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "lifecycle",
                    Summary: summary,
                    Data: data);
            }

            case "SessionPaused":
            {
                var reason = data is { } el
                    && el.ValueKind == JsonValueKind.Object
                    && el.TryGetProperty("reason", out var r)
                        ? r.GetString()
                        : null;
                return new TelemetryEmit(
                    "session_paused",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "lifecycle",
                    Summary: reason,
                    Data: data);
            }

            case "TurnCompleted":
                return new TelemetryEmit(
                    "turn_completed",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    TurnId: ExtractTurnId(data),
                    OccurredAt: occurredAt,
                    Stage: "pipeline",
                    Summary: SummarizeTurnMetrics(data),
                    Data: data);

            case "TurnStarted":
                return new TelemetryEmit(
                    "turn_started",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    TurnId: ExtractTurnId(data),
                    OccurredAt: occurredAt,
                    Stage: "lifecycle",
                    Data: data);

            case "BargeIn":
                return new TelemetryEmit(
                    "barge_in",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "lifecycle",
                    Data: data);

            case "NetworkStateChanged":
            {
                var state = data is { } el
                    && el.ValueKind == JsonValueKind.Object
                    && el.TryGetProperty("state", out var s)
                        ? s.GetString()
                        : null;
                var name = state switch
                {
                    "reconnecting" => "network_degraded",
                    "reconnected" => "network_recovered",
                    _ => "network_state_changed",
                };
                return new TelemetryEmit(
                    name,
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "lifecycle",
                    Data: data);
            }

            case "SpeechCreated":
                return new TelemetryEmit(
                    "speech_created",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "tts",
                    Data: data);

            case "ConversationItemAdded":
                return new TelemetryEmit(
                    "conversation_item_added",
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Stage: "lifecycle",
                    Data: data);

            default:
                return new TelemetryEmit(
                    SnakeCase(eventType),
                    EventSource.Agent,
                    BotId: botId,
                    ConversationId: conversationId,
                    OccurredAt: occurredAt,
                    Data: data);
        }
    }

    /// <summary>
    /// Maps the LiveKit close reason (data.reason) to one of
    /// <see cref="ConversationClosedReasons"/>. Previously inlined in
    /// LiveKitEndpoints.cs.
    /// </summary>
    public static string MapCloseReason(JsonElement? data)
    {
        if (data is not { } element || element.ValueKind != JsonValueKind.Object)
            return ConversationClosedReasons.UserAborted;

        if (!element.TryGetProperty("reason", out var reasonEl))
            return ConversationClosedReasons.UserAborted;

        var reason = reasonEl.GetString() ?? string.Empty;
        var lower = reason.ToLowerInvariant();

        if (lower.Contains("timeout", StringComparison.Ordinal)
            || lower.Contains("away", StringComparison.Ordinal)
            || lower.Contains("shutdown", StringComparison.Ordinal)
            || lower.Contains("no_speech", StringComparison.Ordinal))
        {
            return ConversationClosedReasons.Timeout;
        }

        return ConversationClosedReasons.UserAborted;
    }

    private static Guid? ExtractTurnId(JsonElement? data)
    {
        if (data is not { } el || el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("turnId", out var t) || t.ValueKind != JsonValueKind.String) return null;
        return Guid.TryParse(t.GetString(), out var g) ? g : null;
    }

    // Compact "eou=80 stt=250 llm=1380 tts=540" line for the feed; only present stages.
    private static string? SummarizeTurnMetrics(JsonElement? data)
    {
        if (data is not { } el || el.ValueKind != JsonValueKind.Object) return null;
        var parts = new List<string>(4);
        foreach (var (key, label) in new[]
                 {
                     ("eouMs", "eou"),
                     ("sttMs", "stt"),
                     ("llmMs", "llm"),
                     ("ttsMs", "tts"),
                 })
        {
            var ms = GetInt(el, key);
            if (ms is not null) parts.Add($"{label}={ms}ms");
        }
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string? ExtractStateTransition(JsonElement? data)
    {
        if (data is not { } el || el.ValueKind != JsonValueKind.Object) return null;
        var old = el.TryGetProperty("oldState", out var o) ? o.GetString() : null;
        var next = el.TryGetProperty("newState", out var n) ? n.GetString() : null;
        return old is not null && next is not null ? $"{old} → {next}" : null;
    }

    private static string? ExtractSourceLabel(JsonElement? data)
    {
        if (data is not { } el || el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("source", out var src)) return null;

        var raw = src.ValueKind switch
        {
            JsonValueKind.String => src.GetString(),
            JsonValueKind.Object when src.TryGetProperty("label", out var lbl) => lbl.GetString(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var lower = raw.ToLowerInvariant();
        if (lower.Contains("stt")) return "stt";
        if (lower.Contains("tts")) return "tts";
        if (lower.Contains("llm") || lower.Contains("gemini")) return "llm";
        return lower;
    }

    private static string? ExtractOperatorMessage(JsonElement? data)
    {
        if (data is not { } el || el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("operatorMessage", out var msg)) return null;
        return msg.ValueKind == JsonValueKind.String ? msg.GetString() : null;
    }

    private static string? ExtractError(JsonElement? data)
    {
        if (data is not { } el || el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("error", out var errEl)) return null;
        return errEl.ValueKind switch
        {
            JsonValueKind.String => errEl.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => errEl.GetRawText(),
            _ => errEl.ToString(),
        };
    }

    private static (string? Stage, int? LatencyMs, string? Provider) ExtractMetrics(JsonElement? data)
    {
        if (data is not { } el || el.ValueKind != JsonValueKind.Object) return (null, null, null);
        if (!el.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Object)
            return (null, null, null);

        var type = metrics.TryGetProperty("type", out var t) ? t.GetString() : null;
        var stage = type switch
        {
            "stt_metrics" => "stt",
            "llm_metrics" => "llm",
            "tts_metrics" => "tts",
            "eou_metrics" => "eou",
            "vad_metrics" => "vad",
            _ => null,
        };

        int? latencyMs = stage switch
        {
            "stt" => GetInt(metrics, "transcriptionDelayMs") ?? GetInt(metrics, "durationMs") ?? GetInt(metrics, "audioDurationMs"),
            "llm" => GetInt(metrics, "ttftMs") ?? GetInt(metrics, "inferenceDurationTotalMs") ?? GetInt(metrics, "durationMs"),
            "tts" => GetInt(metrics, "ttfbMs") ?? GetInt(metrics, "durationMs"),
            "eou" => GetInt(metrics, "durationMs"),
            "vad" => GetInt(metrics, "durationMs"),
            _ => null,
        };

        var provider = metrics.TryGetProperty("provider", out var p) ? p.GetString() : null;
        return (stage, latencyMs, provider);
    }

    private static int? GetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : (int?)(v.GetDouble()),
            JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
            _ => null,
        };
    }

    private static string SnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
