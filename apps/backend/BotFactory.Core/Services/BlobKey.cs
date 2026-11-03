namespace BotFactory.Core.Services;

/// <summary>
/// Reads the per-upload version out of a storage key.
///
/// Every upload path mints a fresh <c>Guid.NewGuid()</c> and places it as the
/// second-to-last segment of the key, e.g.
///   {org}/{botId}/branding/logo/{guid}/{filename}
///   {org}/{botId}/thinking-sound/{guid}/{filename}
///   {org}/branding/favicon/{guid}/{filename}
///   {org}/{botId}/{guid}/{filename}          (KB documents)
/// so that GUID already is a value which provably changes whenever the bytes
/// do — no new column, no migration, and every asset already in production has
/// one.
///
/// Surfacing it lets clients address an asset as ...?v={version}, which is what
/// makes a year-long immutable cache safe on an otherwise stable download URL.
/// </summary>
public static class BlobKey
{
    /// <summary>
    /// The version segment, or null if the key is absent or not shaped as
    /// expected. Null is not an error: callers fall back to revalidation, which
    /// is correct for any key this cannot read.
    /// </summary>
    public static string? VersionOf(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;

        var candidate = segments[^2];
        return Guid.TryParse(candidate, out var guid) ? guid.ToString("N") : null;
    }
}
