namespace BotFactory.Core.Services;

/// <summary>
/// Thrown when an external AI provider (Gemini, ElevenLabs) returns a classified failure.
/// </summary>
public sealed class ProviderServiceException : Exception
{
    public ProviderErrorInfo Info { get; }

    public ProviderServiceException(ProviderErrorInfo info, string? rawMessage = null)
        : base(rawMessage ?? info.OperatorMessage)
    {
        Info = info;
    }
}
