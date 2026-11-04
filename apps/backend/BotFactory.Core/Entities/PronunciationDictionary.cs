using System.Text;
using System.Text.RegularExpressions;

namespace BotFactory.Core.Entities;

/// <summary>
/// Parsing, validation and one-shot application of <see cref="Bot.PronunciationRules"/>: how the
/// voice should say what the text writes (an acronym the synthesiser reads as a word: «ABC» as
/// «abse», when it should be «a, be, ce»). One rule per line, <c>written → spoken</c>; the
/// replacement is applied only to what goes to the voice, never to the chat or the stored text.
/// </summary>
/// <remarks>
/// A rule replaces whole words, whatever their case («ABC» also matches «abc» but not
/// «abcdario»), the longest rule first, and what it puts in is not looked at again, so a rule
/// cannot feed another. <see cref="SpokenPronunciation"/> does the same on a stream.
/// </remarks>
public static class PronunciationDictionary
{
    public const int MaxRules = 100;
    public const int MaxWrittenLength = 50;
    public const int MaxSpokenLength = 100;

    /// <summary>The canonical separator, and the ones accepted when typing a rule.</summary>
    public const string Arrow = "→";
    private static readonly string[] Separators = [Arrow, "=>", "->", "="];

    public readonly record struct Rule(string Written, string Spoken);

    /// <summary>
    /// The rules of a text, in order. A line that is not a rule (no separator, an empty side) is
    /// skipped here and reported by <see cref="Validate"/>; a repeated written form (without regard
    /// to case) keeps its first rule.
    /// </summary>
    public static List<Rule> Parse(string? raw)
    {
        var rules = new List<Rule>();
        foreach (var line in Lines(raw))
        {
            if (!TrySplit(line, out var written, out var spoken) || written.Length == 0 || spoken.Length == 0)
                continue;
            if (rules.Exists(r => string.Equals(r.Written, written, StringComparison.OrdinalIgnoreCase)))
                continue;
            rules.Add(new Rule(written, spoken));
        }

        return rules;
    }

    /// <summary>Canonical storage form: <c>written → spoken</c>, one rule per line.</summary>
    public static string Normalize(string? raw) =>
        string.Join('\n', Parse(raw).Select(r => $"{r.Written} {Arrow} {r.Spoken}"));

    /// <summary>The first reason a text cannot be saved as rules, or null when it can.</summary>
    public static string? Validate(string? raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var line in Lines(raw))
        {
            if (!TrySplit(line, out var written, out var spoken))
                return $"Pronunciation rule without «{Arrow}» (written {Arrow} spoken): {Shorten(line)}";
            if (written.Length == 0 || spoken.Length == 0)
                return $"Pronunciation rule with an empty side: {Shorten(line)}";
            if (written.Length > MaxWrittenLength)
                return $"Pronunciation rule: the written form is longer than {MaxWrittenLength} characters: {Shorten(written)}";
            if (spoken.Length > MaxSpokenLength)
                return $"Pronunciation rule: the spoken form is longer than {MaxSpokenLength} characters: {Shorten(spoken)}";
            if (!seen.Add(written))
                return $"Pronunciation rule repeated: «{written}» appears more than once.";
            count++;
        }

        return count > MaxRules
            ? $"Too many pronunciation rules ({count}); the limit is {MaxRules}."
            : null;
    }

    /// <summary>The text with the rules applied; null stays null.</summary>
    public static string? Apply(string? text, IReadOnlyList<Rule> rules)
    {
        if (string.IsNullOrEmpty(text) || rules.Count == 0)
            return text;
        var rewriter = new Rewriter(rules);
        return rewriter.Rewrite(text, out _);
    }

    /// <summary>The compiled form of a set of rules, shared by the one-shot and the streaming use.</summary>
    internal sealed class Rewriter
    {
        private readonly Regex _regex;
        private readonly Dictionary<string, string> _spokenByWritten = new(StringComparer.OrdinalIgnoreCase);

        public Rewriter(IReadOnlyList<Rule> rules)
        {
            var keys = new List<string>();
            foreach (var rule in rules)
            {
                var key = Collapse(rule.Written);
                if (key.Length == 0 || !_spokenByWritten.TryAdd(key, rule.Spoken))
                    continue;
                keys.Add(key);
                if (key.Length > MaxKeyLength)
                    MaxKeyLength = key.Length;
            }

            Keys = keys;
            // Longest first, so «Punto ABC» wins over «ABC». A space in a rule matches any run of whitespace.
            var alternatives = keys
                .OrderByDescending(k => k.Length)
                .Select(k => Regex.Escape(k).Replace("\\ ", @"\s+", StringComparison.Ordinal));
            _regex = new Regex(
                $@"(?<![\p{{L}}\p{{N}}_])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        public IReadOnlyList<string> Keys { get; }

        public int MaxKeyLength { get; }

        public string Rewrite(string text, out int replacements)
        {
            var count = 0;
            var result = _regex.Replace(text, m =>
            {
                count++;
                return _spokenByWritten[Collapse(m.Value)];
            });
            replacements = count;
            return result;
        }
    }

    internal static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> Lines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;
        foreach (var line in raw.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length > 0)
                yield return line;
        }
    }

    // The first separator in the line; where two start at the same place the longer one («=>» before «=»).
    private static bool TrySplit(string line, out string written, out string spoken)
    {
        written = spoken = string.Empty;
        var at = -1;
        var length = 0;
        foreach (var separator in Separators)
        {
            var index = line.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
                continue;
            if (at < 0 || index < at || (index == at && separator.Length > length))
            {
                at = index;
                length = separator.Length;
            }
        }

        if (at < 0)
            return false;
        written = Collapse(line[..at]);
        spoken = Collapse(line[(at + length)..]);
        return true;
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
