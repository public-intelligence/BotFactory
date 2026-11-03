namespace BotFactory.Core.Entities;

/// <summary>
/// Parsing/validation for <see cref="Bot.AllowedEmbedOrigins"/>: a comma or
/// newline separated list of web origins allowed to iframe the bot. Stored as
/// a single normalized comma-separated string on the entity.
/// </summary>
public static class EmbedOrigins
{
    private static readonly char[] Separators = [',', '\n', '\r', ';', ' '];

    /// <summary>Splits and normalizes; silently drops empty entries.</summary>
    public static List<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var result = new List<string>();
        foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var origin = NormalizeOrigin(part);
            if (origin is not null && !result.Contains(origin))
                result.Add(origin);
        }
        return result;
    }

    /// <summary>Canonical storage form: normalized origins joined with commas.</summary>
    public static string Normalize(string? raw) => string.Join(',', Parse(raw));

    /// <summary>
    /// Returns the entries that are NOT valid http(s) origins, so the API can
    /// reject a save instead of silently dropping what the user typed.
    /// </summary>
    public static List<string> InvalidEntries(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var invalid = new List<string>();
        foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (NormalizeOrigin(part) is null)
                invalid.Add(part);
        }
        return invalid;
    }

    /// <summary>
    /// A valid entry is a bare http(s) origin — scheme + host + optional port,
    /// no path/query/fragment/credentials (e.g. "https://ministerio.gob.do").
    /// </summary>
    public static string? NormalizeOrigin(string value)
    {
        var candidate = value.Trim().TrimEnd('/');
        if (candidate.Length == 0) return null;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;

        // GetLeftPart(Authority) yields scheme://host[:port] with default ports stripped.
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}
