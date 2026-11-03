namespace BotFactory.Core.DTOs;

public record SystemLogEntryResponse(
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception,
    string? Category);

public record SystemLogTailResponse(
    List<SystemLogEntryResponse> Entries,
    long FileSizeBytes,
    string FileName);
