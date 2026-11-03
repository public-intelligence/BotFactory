using BotFactory.Core.DTOs;

namespace BotFactory.Core.Services;

public interface IConversationService
{
    Task<bool> PauseAsync(int conversationId, string pausedReason, CancellationToken cancellationToken = default);
    Task<bool> ResumeAsync(int conversationId, CancellationToken cancellationToken = default);
    Task<bool> CloseAsync(int conversationId, string closedReason, CancellationToken cancellationToken = default);
    Task<PaginatedResponse<ConversationResponse>> GetByBotIdAsync(
        int botId,
        int page,
        int pageSize,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null);
    Task<ConversationDetailResponse?> GetByIdAsync(int botId, int conversationId, bool includeEvents = false);
}
