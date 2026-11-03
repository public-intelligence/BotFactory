namespace BotFactory.Core.Services;

/// <summary>
/// Classified provider failure with role-appropriate user copy.
/// </summary>
public sealed record ProviderErrorInfo(
    string ErrorCode,
    string Provider,
    string Stage,
    string UserMessage,
    string OperatorMessage);
