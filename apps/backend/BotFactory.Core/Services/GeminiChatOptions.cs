namespace BotFactory.Core.Services;

/// <summary>
/// Configuration for Gemini chat in the voice pipeline (bound from "Gemini" section).
/// API keys are not stored here — use <c>ServiceType.Llm</c> in app settings.
/// </summary>
public class GeminiChatOptions
{
    public string ChatModel { get; set; } = "gemini-2.5-flash";

    public int HttpTimeoutSeconds { get; set; } = 30;

    public string FallbackMessage { get; set; } = "Lo siento, no pude procesar eso.";

    /// <summary>
    /// Maximum number of KB tool invocations per voice turn (each invocation may require an extra stream pass).
    /// </summary>
    public int MaxToolIterations { get; set; } = 2;

    /// <summary>
    /// Sampling temperature. Lower is more focused and less prone to topic drift
    /// than gemini-2.5-flash's default (~1.0), which suits a factual voice assistant.
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Hard cap on generated tokens per Gemini pass. Sized for spoken replies so
    /// answers stay short for voice. Without a cap the model can ramble.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 512;

    /// <summary>
    /// gemini-2.5-flash "thinking" budget in tokens. 0 disables thinking (lowest
    /// latency, recommended for voice); -1 lets the model decide dynamically.
    /// Caution: with thinking enabled, <see cref="MaxOutputTokens"/> must also
    /// cover thinking tokens or the model can return empty text.
    /// </summary>
    public int ThinkingBudget { get; set; }
}
