namespace BotFactory.Core.Services;

public interface IUrlContentFetcher
{
    /// <summary>
    /// Fetches a URL and returns cleaned plain text, or null when the response is unusable.
    /// </summary>
    Task<UrlContentFetchResult> FetchPlainTextAsync(string url, CancellationToken ct = default);
}

public record UrlContentFetchResult(bool Success, string? PlainText, string? Error, bool Transient = false);
