namespace BotFactory.Core.Services;

/// <summary>
/// Configuration for Gemini chat in the voice pipeline (bound from "Gemini" section).
/// API keys are not stored here — use <c>ServiceType.Llm</c> in app settings.
/// </summary>
public class GeminiChatOptions
{
    public string ChatModel { get; set; } = "gemini-2.5-flash";

    /// <summary>
    /// Limit for one Gemini call, from the request until the last streamed chunk.
    /// A call that runs out of time is retried once (see <see cref="RetryDelayMilliseconds"/>).
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How long to wait for Gemini to start answering (the response headers and the first
    /// streamed chunk) before giving up on that call and retrying it once. A normal call
    /// starts answering in 1-2 s; once in about seventy, Gemini sat on a request for 25-35 s
    /// before answering, and the visitor waited the whole time. Once the first chunk arrives,
    /// only <see cref="HttpTimeoutSeconds"/> applies. 0 turns this limit off. The call that
    /// was cut may already have counted its input tokens, so the retry spends them again.
    /// </summary>
    public int FirstByteTimeoutSeconds { get; set; } = 10;

    public string FallbackMessage { get; set; } = "Lo siento, no pude procesar eso.";

    /// <summary>
    /// Maximum number of tool executions per turn, counting every call of a pass in
    /// which the model asked for several at once. When the budget runs out the turn
    /// does not fail: one last pass, with tools forbidden, answers with what was gathered.
    /// </summary>
    public int MaxToolIterations { get; set; } = 6;

    /// <summary>
    /// Wait before the single retry of a Gemini call that failed transiently (5xx,
    /// timeout, network). A 429 waits twice as long, or as long as the provider asked when it
    /// said (see <see cref="QuotaRetryMaxWaitMilliseconds"/>). 0 retries immediately.
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 1500;

    /// <summary>
    /// The longest wait a 429 that says how long to wait is worth retrying for. A per-minute
    /// quota asks for tens of seconds (the window has to empty), which no visitor will sit
    /// through and a retry a few seconds later cannot beat: such a turn fails at once, with a
    /// clear message, and the call that would have been rejected is not made. A short wait
    /// (a burst limit) is still retried, once, after the time the provider asked for.
    /// A 429 that does not say how long keeps the plain retry.
    /// </summary>
    public int QuotaRetryMaxWaitMilliseconds { get; set; } = 5000;

    /// <summary>
    /// Sampling temperature. Lower is more focused and less prone to topic drift
    /// than gemini-2.5-flash's default (~1.0), which suits a factual voice assistant.
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Sampling temperature for bots with «Respaldo obligatorio»: closer to the sources,
    /// less room to embellish.
    /// </summary>
    public double GroundedTemperature { get; set; } = 0.1;

    /// <summary>
    /// Bots with «Respaldo obligatorio» re-send the tool results of this many previous
    /// turns, each trimmed to <see cref="HistoryToolResultChars"/> characters.
    /// </summary>
    public int HistoryToolTurns { get; set; } = 3;

    public int HistoryToolResultChars { get; set; } = 2000;

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
