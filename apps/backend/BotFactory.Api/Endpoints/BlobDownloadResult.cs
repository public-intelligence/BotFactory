using BotFactory.Core.Services;
using Microsoft.Net.Http.Headers;

namespace BotFactory.Api.Endpoints;

/// <summary>
/// Shared response shaping for blob-backed download endpoints: a Cache-Control
/// tier plus the ETag / Last-Modified validators. Results.File handles the
/// conditional-request side (If-None-Match / If-Modified-Since → 304) itself.
/// </summary>
internal static class BlobDownloadResult
{
    /// <summary>
    /// Authenticated admin routes. The download URL is stable and serves whatever
    /// the *current* asset is (the frontend's ?v= cache-buster is the filename, so
    /// re-uploading a same-named file would not change it), so admins must never
    /// see a stale asset: no-cache forces revalidation on every use, which is a
    /// cheap 304 unless the blob actually changed. private keeps intermediaries
    /// from ever caching a tenant's asset.
    /// </summary>
    public const string AdminCacheControl = "private, no-cache";

    /// <summary>
    /// Anonymous by-slug routes (public chat / embed). Shared caches may store;
    /// max-age bounds how long a re-uploaded asset can stay stale for visitors,
    /// after which the ETag keeps refreshes down to 304s.
    /// </summary>
    public const string PublicCacheControl = "public, max-age=300";

    /// <summary>
    /// A year, for a request that named the exact version it wanted. Only ever
    /// sent when ?v= matches the asset's current version, which makes the URL
    /// content-addressed for the duration: a re-upload mints a new version, so
    /// the client asks for a different URL rather than being stuck with this one.
    /// </summary>
    private const string ImmutableCacheControl = "max-age=31536000, immutable";

    /// <param name="seekable">
    /// Buffers the blob so the response can serve HTTP range requests. Off by
    /// default: nothing ever sends a Range for an &lt;img&gt;, so paying the
    /// memory for logos and favicons would be waste.
    ///
    /// Audio is the exception. Safari treats byte-range support as a
    /// precondition for playing media rather than an optimisation — it probes
    /// with a Range request and can decline to play at all when the server
    /// answers 200 with the whole body. The old SAS URLs went straight to
    /// storage, which honours Range natively; proxying the bytes through the
    /// API is what dropped it. We have no use for seeking ourselves.
    ///
    /// A MemoryStream can seek, which is the only thing ASP.NET needs before it
    /// takes over the entire protocol — Range parsing, If-Range, 206 with
    /// Content-Range, Accept-Ranges, 416 on unsatisfiable. None of that is ours
    /// to write. Buffering (rather than a seekable stream from storage) keeps
    /// this to the one round-trip DownloadStreamingAsync already made: asking
    /// storage for a seekable stream would cost a second call for the metadata
    /// and reintroduce the delete-mid-request race that call was collapsed to
    /// avoid. Upload caps bound the cost at 5MB, and versioned URLs are cached
    /// immutably, so a given client buffers a given asset at most once.
    /// </param>
    public static async Task<IResult> StreamAsync(
        HttpContext http,
        BlobDownload? download,
        string cacheControl,
        string? fileDownloadName = null,
        bool seekable = false,
        CancellationToken ct = default)
    {
        if (download is null)
            return Results.NotFound();

        // Immutable is unrevocable — there is no purging a browser cache — so it
        // is gated on the caller proving it knows the current version. A request
        // with no ?v= (a bookmark, a hand-typed URL, a still-loaded older SPA
        // build) or a stale one gets the revalidating tier and still sees the
        // current asset; it just pays a 304 for the privilege.
        var requested = http.Request.Query["v"].ToString();
        var versioned = !string.IsNullOrEmpty(requested)
            && download.Version is not null
            && string.Equals(requested, download.Version, StringComparison.Ordinal);

        if (versioned)
            cacheControl = $"{Scope(cacheControl)}, {ImmutableCacheControl}";

        http.Response.Headers.CacheControl = cacheControl;
        var tag = EntityTagHeaderValue.TryParse(download.ETag, out var etag) ? etag : null;

        if (!seekable)
        {
            return Results.File(
                download.Content,
                download.ContentType,
                fileDownloadName,
                lastModified: download.LastModified,
                entityTag: tag);
        }

        var buffer = new MemoryStream();
        await using (download.Content)
        {
            await download.Content.CopyToAsync(buffer, ct);
        }
        buffer.Position = 0;

        return Results.File(
            buffer,
            download.ContentType,
            fileDownloadName,
            lastModified: download.LastModified,
            entityTag: tag,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// public/private carries over to the immutable tier unchanged: a tenant's
    /// asset must stay out of shared caches however long it is cacheable for.
    /// </summary>
    private static string Scope(string cacheControl) =>
        cacheControl.StartsWith("public", StringComparison.Ordinal) ? "public" : "private";
}
