namespace BotFactory.Core.Services;

public record KbScraperRenderResult(
    bool Success,
    string? Html,
    int StatusCode,
    string? FinalUrl,
    string? Error,
    bool Transient = false);

public interface IKbScraperClient
{
    Task<KbScraperRenderResult> RenderAsync(string url, CancellationToken ct = default);
}
