using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BotFactory.Core.Services;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Services;

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorageService(IOptions<BlobStorageOptions> options)
    {
        var opts = options.Value;
        var service = new BlobServiceClient(opts.ConnectionString);
        _container = service.GetBlobContainerClient(opts.ContainerName);
        _container.CreateIfNotExists();
    }

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        => await _container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

    public async Task DeleteAsync(string key, CancellationToken ct = default)
        => await _container.DeleteBlobIfExistsAsync(key, cancellationToken: ct);

    public async Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        var response = await _container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    public async Task<BlobDownload?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        // Single round-trip, race-free: with a remote store, existence is only
        // knowable as a side effect of the operation itself, so a prior Exists /
        // GetProperties check would already be stale by the time we download.
        try
        {
            var response = await _container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct);
            var details = response.Value.Details;
            var contentType = string.IsNullOrWhiteSpace(details.ContentType)
                ? "application/octet-stream"
                : details.ContentType;
            return new BlobDownload(
                response.Value.Content,
                contentType,
                details.ETag.ToString("H"),
                details.LastModified,
                BlobKey.VersionOf(key));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
