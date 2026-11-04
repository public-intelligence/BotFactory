namespace BotFactory.Core.Entities;

/// <summary>
/// Parsing and validation for <see cref="Bot.TranscriptionVocabulary"/>: the words the speech
/// recogniser should favour for this bot (its own name, institutions, acronyms and place names
/// it would otherwise hear as ordinary words). Typed as one term per
/// line, stored as one normalized newline-separated string, and sent to the voice agent as a list.
/// </summary>
/// <remarks>
/// The limits are the recogniser's for live transcription (50 terms of up to 20 characters);
/// a term over them is rejected at save time instead of being cut or, worse, refused by the
/// provider in the middle of a call.
/// </remarks>
public static class TranscriptionTerms
{
    public const int MaxTerms = 50;
    public const int MaxTermLength = 20;

    private static readonly char[] Separators = ['\n', '\r', ',', ';'];

    /// <summary>
    /// Splits on lines, commas and semicolons; trims, collapses inner spaces, drops empty
    /// entries and repeats (compared without regard to case; the first spelling is kept).
    /// </summary>
    public static List<string> Parse(string? raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var term = string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (term.Length == 0 || result.Contains(term, StringComparer.OrdinalIgnoreCase))
                continue;
            result.Add(term);
        }

        return result;
    }

    /// <summary>Canonical storage form: the terms, one per line.</summary>
    public static string Normalize(string? raw) => string.Join('\n', Parse(raw));

    /// <summary>The reason a list cannot be saved, or null when it can.</summary>
    public static string? Validate(string? raw)
    {
        var terms = Parse(raw);
        if (terms.Count > MaxTerms)
            return $"Too many transcription terms ({terms.Count}); the limit is {MaxTerms}.";

        var tooLong = terms.Where(t => t.Length > MaxTermLength).ToList();
        if (tooLong.Count > 0)
            return $"Transcription term(s) longer than {MaxTermLength} characters: {string.Join(", ", tooLong)}.";

        return null;
    }
}
