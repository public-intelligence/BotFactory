using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BotFactory.Core.Services;

/// <summary>
/// Maps raw provider HTTP bodies and agent error payloads to stable codes and
/// role-appropriate Spanish messages (parity with agent provider_errors.py).
/// </summary>
public static class ProviderErrorClassifier
{
    public const string UserMessageUnavailable =
        "El servicio no está disponible en este momento. Por favor, intente más tarde.";

    /// <summary>
    /// What the visitor reads (and hears) when the provider's quota is spent: it says what is
    /// happening and what to do, instead of a generic «no pude procesar eso».
    /// </summary>
    public const string UserMessageBusy =
        "Estoy atendiendo muchas consultas en este momento. Intente de nuevo en un minuto.";

    public const string CodeElevenLabsQuotaExceeded = "elevenlabs_quota_exceeded";
    public const string CodeElevenLabsAuthFailed = "elevenlabs_auth_failed";
    public const string CodeGeminiQuotaExceeded = "gemini_quota_exceeded";
    public const string CodeGeminiAuthFailed = "gemini_auth_failed";
    public const string CodeProviderUnavailable = "provider_unavailable";

    /// <summary>
    /// The wait a Gemini error asks for: <c>error.details[].retryDelay</c> of the
    /// <c>google.rpc.RetryInfo</c> detail ("57s", "0.5s"), or, failing that, the
    /// "Please retry in 28.9s" of its message. Null when it does not say. The body may
    /// arrive wrapped in an array, as the streaming endpoint does.
    /// </summary>
    public static TimeSpan? ParseRetryDelay(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                root = root[0];
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
                return null;

            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.ValueKind == JsonValueKind.Object
                        && detail.TryGetProperty("@type", out var type)
                        && type.ValueKind == JsonValueKind.String
                        && (type.GetString() ?? string.Empty).EndsWith("google.rpc.RetryInfo", StringComparison.Ordinal)
                        && detail.TryGetProperty("retryDelay", out var delay)
                        && delay.ValueKind == JsonValueKind.String
                        && ParseSeconds(delay.GetString()) is { } fromDetail)
                    {
                        return fromDetail;
                    }
                }
            }

            if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                var match = RetryInMessage.Match(message.GetString() ?? string.Empty);
                if (match.Success && ParseSeconds(match.Groups[1].Value + "s") is { } fromMessage)
                    return fromMessage;
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's HTML error page, say): the provider did not say.
        }

        return null;
    }

    private static readonly Regex RetryInMessage = new(
        @"retry in ([0-9]+(?:\.[0-9]+)?)s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static TimeSpan? ParseSeconds(string? duration)
    {
        var text = duration?.Trim();
        if (string.IsNullOrEmpty(text) || !text.EndsWith('s'))
            return null;

        return double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
               && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(Math.Max(0, seconds))
            : null;
    }

    public static ProviderErrorInfo ClassifyGemini(int? status, string? body)
    {
        var text = (body ?? string.Empty).ToLowerInvariant();

        if (status == 429
            || text.Contains("resource_exhausted", StringComparison.Ordinal)
            || text.Contains("quota exceeded", StringComparison.Ordinal)
            || text.Contains("quota", StringComparison.Ordinal) && text.Contains("exceed", StringComparison.Ordinal))
        {
            return new ProviderErrorInfo(
                CodeGeminiQuotaExceeded,
                "gemini",
                "llm",
                UserMessageBusy,
                "Gemini: cuota excedida o límite de API alcanzado");
        }

        if (status is 401 or 403
            || text.Contains("api key", StringComparison.Ordinal)
            || text.Contains("permission denied", StringComparison.Ordinal))
        {
            return new ProviderErrorInfo(
                CodeGeminiAuthFailed,
                "gemini",
                "llm",
                UserMessageUnavailable,
                "Gemini: clave API inválida o sin permisos");
        }

        return new ProviderErrorInfo(
            CodeProviderUnavailable,
            "gemini",
            "llm",
            UserMessageUnavailable,
            "Gemini: servicio no disponible (LLM)");
    }

    public static ProviderErrorInfo ClassifyAgentError(string? sourceLabel, string? errorText, int? httpStatus = null)
    {
        var source = (sourceLabel ?? string.Empty).ToLowerInvariant();
        var text = (errorText ?? string.Empty).ToLowerInvariant();
        var isElevenLabs = source.Contains("eleven", StringComparison.Ordinal)
            || source.Contains("stt", StringComparison.Ordinal)
            || source.Contains("tts", StringComparison.Ordinal);
        var isGemini = source.Contains("llm", StringComparison.Ordinal)
            || source.Contains("gemini", StringComparison.Ordinal);

        string stage;
        if (source.Contains("stt", StringComparison.Ordinal))
            stage = "stt";
        else if (source.Contains("tts", StringComparison.Ordinal))
            stage = "tts";
        else if (isGemini)
            stage = "llm";
        else if (isElevenLabs)
            stage = source.Contains("tts", StringComparison.Ordinal) ? "tts" : "stt";
        else
            stage = "agent";

        if (isGemini)
            return ClassifyGemini(httpStatus, errorText);

        if (isElevenLabs)
            return ClassifyElevenLabs(stage, httpStatus, errorText);

        if (httpStatus == 429 || LooksLikeQuota(text))
        {
            return new ProviderErrorInfo(
                CodeProviderUnavailable,
                "unknown",
                stage,
                UserMessageUnavailable,
                "Servicio de voz no disponible");
        }

        return new ProviderErrorInfo(
            CodeProviderUnavailable,
            "unknown",
            stage,
            UserMessageUnavailable,
            $"Servicio no disponible ({stage})");
    }

    private static ProviderErrorInfo ClassifyElevenLabs(string stage, int? status, string? body)
    {
        var text = (body ?? string.Empty).ToLowerInvariant();
        var stageLabel = stage == "tts" ? "TTS" : "STT";

        if (status == 402
            || LooksLikeQuota(text))
        {
            return new ProviderErrorInfo(
                CodeElevenLabsQuotaExceeded,
                "elevenlabs",
                stage,
                UserMessageUnavailable,
                $"ElevenLabs: saldo agotado o cuota excedida ({stageLabel})");
        }

        if (status is 401 or 403
            || text.Contains("invalid api key", StringComparison.Ordinal)
            || text.Contains("unauthorized", StringComparison.Ordinal))
        {
            return new ProviderErrorInfo(
                CodeElevenLabsAuthFailed,
                "elevenlabs",
                stage,
                UserMessageUnavailable,
                $"ElevenLabs: clave API inválida o sin permisos ({stageLabel})");
        }

        return new ProviderErrorInfo(
            CodeProviderUnavailable,
            "elevenlabs",
            stage,
            UserMessageUnavailable,
            $"ElevenLabs: servicio no disponible ({stageLabel})");
    }

    private static bool LooksLikeQuota(string text) =>
        text.Contains("quota", StringComparison.Ordinal)
        || text.Contains("credit", StringComparison.Ordinal)
        || text.Contains("billing", StringComparison.Ordinal)
        || text.Contains("insufficient", StringComparison.Ordinal)
        || text.Contains("balance", StringComparison.Ordinal)
        || text.Contains("payment required", StringComparison.Ordinal);
}
