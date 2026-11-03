namespace BotFactory.Core.Services;

/// <summary>
/// A blob opened for streaming to an HTTP client: the content plus the
/// validators (ETag / Last-Modified) needed for conditional requests.
/// The ETag is in HTTP header format, i.e. including the surrounding quotes.
/// Version is the per-upload token read off the storage key (see <see cref="BlobKey"/>);
/// a request whose ?v= matches it is safe to cache immutably.
/// </summary>
public sealed record BlobDownload(
    Stream Content,
    string ContentType,
    string ETag,
    DateTimeOffset LastModified,
    string? Version);

public interface IBlobStorageService
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Opens a blob for streaming to an HTTP client. Prefer this over SAS URIs when
    /// the browser cannot reach the storage host (e.g. Azurite on the Docker network).
    /// Returns null when the blob does not exist.
    /// </summary>
    Task<BlobDownload?> OpenReadAsync(string key, CancellationToken ct = default);
}
