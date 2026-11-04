namespace BotFactory.Core.Services;

/// <summary>
/// Thrown when an external AI provider (Gemini, ElevenLabs) returns a classified failure.
/// </summary>
public sealed class ProviderServiceException : Exception
{
    public ProviderErrorInfo Info { get; }

    /// <summary>
    /// HTTP status the provider answered with, when there was one. Lets callers tell
    /// a transient failure (429, 5xx) from one that retrying cannot fix (400, 401, 403).
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// How long the provider asked to wait before trying again (Gemini's <c>RetryInfo.retryDelay</c>
    /// or a <c>Retry-After</c> header), when it said. A per-minute quota answers with the time the
    /// window needs to empty, tens of seconds: retrying sooner only repeats the error.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    public ProviderServiceException(
        ProviderErrorInfo info,
        string? rawMessage = null,
        int? statusCode = null,
        TimeSpan? retryAfter = null)
        : base(rawMessage ?? info.OperatorMessage)
    {
        Info = info;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
