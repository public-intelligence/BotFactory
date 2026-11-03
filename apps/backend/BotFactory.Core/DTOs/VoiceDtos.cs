namespace BotFactory.Core.DTOs;

public record VoiceDto(
    string VoiceId,
    string Name,
    string? Description,
    string? PreviewUrl,
    string? Category);

public record ElevenLabsVoicesResponse(
    bool Configured,
    IReadOnlyList<VoiceDto> Voices,
    DateTime? FetchedAt);
