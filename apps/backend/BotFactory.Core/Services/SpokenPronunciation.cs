using System.Text;
using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

/// <summary>
/// Applies a bot's pronunciation rules (<see cref="PronunciationDictionary"/>) to text as it
/// streams to the voice. The result is the same whatever way the text arrives in chunks: the end
/// of what has arrived is held back while it could still become part of a rule (a trailing word
/// that may continue, or the start of a longer rule such as «Punto AB» for «Punto ABC»), and
/// released with the next chunk or at the end of the turn.
/// </summary>
public sealed class SpokenPronunciation
{
    private readonly PronunciationDictionary.Rewriter? _rewriter;
    private readonly StringBuilder _pending = new();

    public SpokenPronunciation(IReadOnlyList<PronunciationDictionary.Rule> rules)
    {
        if (rules.Count > 0)
            _rewriter = new PronunciationDictionary.Rewriter(rules);
    }

    /// <summary>How many times a rule has been applied so far.</summary>
    public int Replacements { get; private set; }

    /// <summary>Push a chunk of spoken text; yields what is safe to send on (at most one chunk).</summary>
    public IEnumerable<string> Push(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            yield break;
        if (_rewriter is null)
        {
            yield return chunk;
            yield break;
        }

        _pending.Append(chunk);
        var text = _pending.ToString();
        var safe = SafeLength(text);
        if (safe <= 0)
            yield break;

        yield return Rewrite(text[..safe]);
        _pending.Remove(0, safe);
    }

    /// <summary>End of the turn: everything that was held back.</summary>
    public IEnumerable<string> Flush()
    {
        if (_pending.Length == 0)
            yield break;
        var text = _pending.ToString();
        _pending.Clear();
        yield return _rewriter is null ? text : Rewrite(text);
    }

    private string Rewrite(string text)
    {
        var result = _rewriter!.Rewrite(text, out var count);
        Replacements += count;
        return result;
    }

    // The part of the text that cannot change when more arrives.
    private int SafeLength(string text)
    {
        var safe = text.Length;

        // A word at the very end may continue in the next chunk («ABC» + «DEF»).
        var wordStart = text.Length;
        while (wordStart > 0 && IsWordChar(text[wordStart - 1]))
            wordStart--;
        if (wordStart < text.Length)
            safe = wordStart;

        // The end of the text may be the start of a longer rule («Punto AB» for «Punto ABC»).
        for (var start = Math.Max(0, text.Length - _rewriter!.MaxKeyLength); start < text.Length; start++)
        {
            // A rule starts with a character of its own, never with a space, and at the start of a word.
            if (char.IsWhiteSpace(text[start]) || (start > 0 && IsWordChar(text[start - 1]) && IsWordChar(text[start])))
                continue;
            var suffix = PronunciationDictionary.Collapse(text[start..]);
            if (text[^1] is ' ' or '\t' && !suffix.EndsWith(' '))
                suffix += ' ';
            if (suffix.Length == 0)
                continue;
            if (_rewriter.Keys.Any(k => k.StartsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                safe = Math.Min(safe, start);
                break;
            }
        }

        return safe;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
