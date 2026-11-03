namespace BotFactory.Core.Services;

public record ChatTurnExecuteRequest(
    int ConversationId,
    string Text,
    Guid TurnId);

public record ChatTurnProviderError(
    string ErrorCode,
    string UserMessage,
    string? OperatorMessage);

public record ChatTurnResult(
    string Content,
    string Provider,
    int? TtftMs,
    int TotalMs,
    int ToolInvokes,
    IReadOnlyList<object> Segments,
    ChatTurnProviderError? ProviderError,
    bool WasCancelled);

public interface IChatTurnService
{
    Task<ChatTurnResult> ExecuteAsync(
        ChatTurnExecuteRequest request,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken cancellationToken);
}
