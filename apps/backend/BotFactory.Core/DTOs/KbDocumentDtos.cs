namespace BotFactory.Core.DTOs;

public record KbDocumentResponse(
    int Id,
    int BotId,
    string Filename,
    string Status,
    string SourceType,
    string? SourceUrl,
    DateTime? IndexedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ErrorMessage);

public record KbDocumentContentResponse(string Filename, string Content);

public record CreateKbDocumentsFromUrlsRequest(IReadOnlyList<string> Urls);

public record CreateKbDocumentsFromUrlsResponse(
    IReadOnlyList<KbDocumentResponse> Created,
    int SkippedExistingCount,
    int SkippedPdfCount = 0);

public record BulkKbDocumentsRequest(
    IReadOnlyList<int>? DocumentIds = null,
    string? Status = null,
    string? SourceType = null,
    string? Search = null);

public record BulkKbDocumentsResponse(int AffectedCount);
