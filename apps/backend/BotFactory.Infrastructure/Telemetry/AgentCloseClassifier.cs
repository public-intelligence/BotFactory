using System.Text.Json;

namespace BotFactory.Infrastructure.Telemetry;

/// <summary>
/// Classifies agent <c>Close</c> telemetry so dual-mode bots can keep the
/// conversation active for text after a benign voice-session disconnect.
/// </summary>
public static class AgentCloseClassifier
{
    public static bool HasCloseError(JsonElement? data)
    {
        if (data is not { } element || element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty("error", out var errorEl)
            && errorEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(errorEl.GetString()))
        {
            return true;
        }

        return false;
    }

    public static bool IsBenignVoiceSessionEndCloseData(JsonElement? data)
    {
        if (data is not { } element || element.ValueKind != JsonValueKind.Object)
            return false;

        if (HasCloseError(data))
            return false;

        if (!element.TryGetProperty("reason", out var reasonEl))
            return false;

        var reason = reasonEl.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        // LiveKit CloseReason enum value (e.g. "10" = client-initiated disconnect).
        if (reason.All(char.IsDigit))
            return true;

        var lower = reason.ToLowerInvariant();
        if (lower.Contains("failed", StringComparison.Ordinal)
            || lower.Contains("error", StringComparison.Ordinal))
        {
            return false;
        }

        return lower.Contains("disconnect", StringComparison.Ordinal)
            || lower.Contains("left", StringComparison.Ordinal)
            || lower.Contains("leave", StringComparison.Ordinal)
            || lower.Contains("room", StringComparison.Ordinal)
            || lower.Contains("participant", StringComparison.Ordinal)
            || lower.Contains("departure", StringComparison.Ordinal)
            || lower.Contains("client_request", StringComparison.Ordinal);
    }
}
