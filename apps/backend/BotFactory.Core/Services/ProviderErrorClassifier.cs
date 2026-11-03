namespace BotFactory.Core.Services;

/// <summary>
/// Maps raw provider HTTP bodies and agent error payloads to stable codes and
/// role-appropriate Spanish messages (parity with agent provider_errors.py).
/// </summary>
public static class ProviderErrorClassifier
{
    public const string UserMessageUnavailable =
        "El servicio no está disponible en este momento. Por favor, intenta más tarde.";

    public const string CodeElevenLabsQuotaExceeded = "elevenlabs_quota_exceeded";
    public const string CodeElevenLabsAuthFailed = "elevenlabs_auth_failed";
    public const string CodeGeminiQuotaExceeded = "gemini_quota_exceeded";
    public const string CodeGeminiAuthFailed = "gemini_auth_failed";
    public const string CodeProviderUnavailable = "provider_unavailable";

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
                UserMessageUnavailable,
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
