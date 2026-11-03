namespace BotFactory.Core.DTOs;

public record PaginatedResponse<T>(
    List<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
