using System.Text;
using System.Text.RegularExpressions;

namespace BotFactory.Core.Services;

/// <summary>
/// Streaming rewrite for voice TTS. What a voice cannot say well (links, e-mail addresses,
/// phone numbers, service codes such as *462) is taken out of the spoken text. The caller is
/// pointed to the chat only when the answer carries the contact line the bot writes when it
/// gives contact details, «Por favor, vea los datos de contacto:»: that line, and everything
/// contact-like after it (short numbers included), becomes one phrase, «Puede ver el teléfono
/// en el chat.». Anywhere else nothing is announced, and numbers a voice reads fine (years,
/// decrees, amounts) stay in the audio. Stored/chat text is untouched (the sanitizer runs
/// only on the LiveKit SSE path).
/// </summary>
public sealed class VoiceSpokenSanitizer
{
    // Explicit schemes, www., or bare hostnames with a plausible TLD (e.g. puravidacr.go.cr).
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""']+"
        + @"|www\.[^\s<>""']+"
        + @"|\b[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+(?:/[^\s<>""']*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmailRegex = new(
        @"\b[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Leading + / digits with common separators; validated by digit count (7–15).
    private static readonly Regex PhoneCandidateRegex = new(
        @"\+?\d[\d\s\-().]{5,22}\d",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Short service numbers (e.g. 1311), optional parentheses.
    private static readonly Regex ShortPhoneRegex = new(
        @"\(?\b\d{3,6}\b\)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The line the bot writes when it gives contact details: the only place numbers are announced.
    private static readonly Regex ContactLineMarker = new(
        @"por\s+favor,?\s+vea\s+los\s+datos\s+de\s+contacto\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The marker as it is compared while it may still be arriving: lowercase, no comma.
    private const string MarkerCanonical = "por favor vea los datos de contacto:";

    // A service code written with its asterisk (*462).
    private static readonly Regex ServiceCodeRegex = new(
        @"\*\d{2,6}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Digits alone in parentheses, the written copy of what was just said in words:
    // «el asterisco cuatro, seis, dos (*462)», «nueve, uno, uno (911)».
    private static readonly Regex ParenthesizedDigitsRegex = new(
        @"\(\s*\*?\d{2,6}\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Separators between the items of the contact line (« · »), left over once they are gone.
    private static readonly Regex LineSeparatorsRegex = new(
        @"\s*[·•|]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforePunctuationRegex = new(
        @"\s+([.,;:!?])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<char> TrailingUrlJunk = new(".,;:!?)]}'\"»");

    private static readonly HashSet<string> KnownTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "org", "net", "edu", "gov", "io", "app", "info", "lat", "do", "cr", "es", "mx",
    };

    private static readonly HashSet<string> KnownMultiTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "gob.do", "go.cr", "com.do", "com.cr", "gov.do",
    };

    private readonly StringBuilder _pending = new();
    private readonly StringBuilder _heldFromFirstContact = new();
    private bool _holdingFromFirstContact;

    /// <summary>Push a streaming delta; yields any spoken chunks safe to send to TTS.</summary>
    public IEnumerable<string> Push(string delta)
    {
        if (string.IsNullOrEmpty(delta))
            yield break;

        if (_holdingFromFirstContact)
        {
            _heldFromFirstContact.Append(delta);
            yield break;
        }

        _pending.Append(delta);
        foreach (var chunk in DrainPending(holdIncompleteTail: true))
            yield return chunk;
    }

    /// <summary>Flush remaining buffered text at end of turn.</summary>
    public IEnumerable<string> Flush()
    {
        if (_holdingFromFirstContact)
        {
            _heldFromFirstContact.Append(_pending);
            _pending.Clear();
            var rewritten = RewriteFromFirstContact(_heldFromFirstContact.ToString());
            _heldFromFirstContact.Clear();
            _holdingFromFirstContact = false;
            if (!string.IsNullOrEmpty(rewritten))
                yield return rewritten;
            yield break;
        }

        foreach (var chunk in DrainPending(holdIncompleteTail: false))
            yield return chunk;
    }

    /// <summary>One-shot sanitize (tests / non-streaming).</summary>
    public static string Sanitize(string text)
    {
        var s = new VoiceSpokenSanitizer();
        var sb = new StringBuilder();
        foreach (var chunk in s.Push(text))
            sb.Append(chunk);
        foreach (var chunk in s.Flush())
            sb.Append(chunk);
        return sb.ToString();
    }

    /// <summary>
    /// The phrase that takes the place of the contact line, in «usted» like the rest of the
    /// bot's answers. With nothing recognisable after the line, it names the contact details.
    /// </summary>
    public static string BuildPhrase(int urlCount, int phoneCount, int emailCount = 0)
    {
        var parts = new List<string>(3);
        if (urlCount == 1) parts.Add("el enlace");
        else if (urlCount > 1) parts.Add("los enlaces");

        if (emailCount == 1) parts.Add("el correo");
        else if (emailCount > 1) parts.Add("los correos");

        if (phoneCount == 1) parts.Add("el teléfono");
        else if (phoneCount > 1) parts.Add("los teléfonos");

        if (parts.Count == 0)
            return "Puede ver los datos de contacto en el chat.";
        if (parts.Count == 1)
            return $"Puede ver {parts[0]} en el chat.";
        if (parts.Count == 2)
            return $"Puede ver {parts[0]} y {parts[1]} en el chat.";
        return $"Puede ver {parts[0]}, {parts[1]} y {parts[2]} en el chat.";
    }

    private IEnumerable<string> DrainPending(bool holdIncompleteTail)
    {
        while (_pending.Length > 0)
        {
            var text = _pending.ToString();

            // End of turn: rewrite the whole buffer in one shot (correct counts,
            // no "hold for later" that Flush would never finish).
            if (!holdIncompleteTail)
            {
                _pending.Clear();
                var rewritten = RewriteFromFirstContact(text);
                if (!string.IsNullOrEmpty(rewritten))
                    yield return rewritten;
                yield break;
            }

            var scanLen = LengthWithoutIncompleteTail(text);
            if (scanLen <= 0)
                yield break;

            var scan = text[..scanLen];
            if (!TryFindNextContact(scan, 0, out var match))
            {
                // No contact in the safe prefix — emit it and keep any held tail.
                if (scanLen == text.Length)
                {
                    _pending.Clear();
                    if (scan.Length > 0)
                        yield return scan;
                }
                else
                {
                    _pending.Remove(0, scanLen);
                    if (scan.Length > 0)
                        yield return scan;
                }
                yield break;
            }

            // Emit text before the first contact, then hold from the contact onward so Flush
            // can build the phrase with full counts. The space just before the contact is held
            // too: whether it stays depends on what follows once the contact is gone («Ver
            // https://x.gob.do.» must end «Ver.», not «Ver .»).
            var holdFrom = match.Index;
            while (holdFrom > 0 && char.IsWhiteSpace(scan[holdFrom - 1]))
                holdFrom--;
            if (holdFrom > 0)
                yield return scan[..holdFrom];

            _holdingFromFirstContact = true;
            _heldFromFirstContact.Append(text[holdFrom..]);
            _pending.Clear();
            yield break;
        }
    }

    private static string RewriteFromFirstContact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var contacts = FindAllContacts(text, out var lineStart);
        if (contacts.Count == 0)
            return text;

        // Only the contact line announces anything: what it holds is counted for the phrase,
        // which takes the place of its marker. Outside it, what a voice cannot say (links,
        // addresses, phone numbers, service codes) is dropped without a word.
        var hasLine = lineStart != int.MaxValue;
        var urlCount = 0;
        var phoneCount = 0;
        var emailCount = 0;
        foreach (var c in contacts)
        {
            if (c.Index < lineStart)
                continue;
            switch (c.Kind)
            {
                case ContactKind.Url: urlCount++; break;
                case ContactKind.Phone: phoneCount++; break;
                case ContactKind.Email: emailCount++; break;
            }
        }

        var phrase = hasLine ? BuildPhrase(urlCount, phoneCount, emailCount) : string.Empty;
        var sb = new StringBuilder(text.Length + phrase.Length);
        var last = 0;
        foreach (var c in contacts)
        {
            sb.Append(text, last, c.Index - last);
            if (c.Kind == ContactKind.Marker)
                sb.Append(phrase);
            last = c.Index + c.Length;
        }

        sb.Append(text, last, text.Length - last);

        var result = CollapseWhitespace(sb.ToString());
        if (hasLine)
            result = CollapseWhitespace(LineSeparatorsRegex.Replace(result, " "));
        result = SpaceBeforePunctuationRegex.Replace(result, "$1");

        // The text held back began with the space that followed what was already spoken.
        if (result.Length > 0 && char.IsWhiteSpace(text[0]) && result[0] is not ('.' or ',' or ';' or ':' or '!' or '?'))
            result = " " + result;
        return result;
    }

    private static List<ContactMatch> FindAllContacts(string text, out int lineStart)
    {
        var list = new List<ContactMatch>();
        var covered = new bool[text.Length];

        // The contact line, when there is one, starts at its marker; numbers as short as
        // three digits count as contacts only from there on.
        lineStart = int.MaxValue;
        var marker = ContactLineMarker.Match(text);
        if (marker.Success)
        {
            lineStart = marker.Index;
            Mark(covered, marker.Index, marker.Length);
            list.Add(new ContactMatch(ContactKind.Marker, marker.Index, marker.Length));
        }

        foreach (Match m in UrlRegex.Matches(text))
        {
            var (start, coreLen) = TrimUrlMatch(text, m.Index, m.Length);
            if (coreLen <= 0) continue;
            // Don't treat the domain half of an email as a bare URL.
            if (start > 0 && text[start - 1] == '@') continue;
            var core = text.Substring(start, coreLen);
            if (!IsPlausibleUrl(core)) continue;
            // Only the link itself: the full stop that follows it belongs to the sentence.
            var replaceLen = coreLen;
            if (Overlaps(covered, start, replaceLen))
                continue;
            Mark(covered, start, replaceLen);
            list.Add(new ContactMatch(ContactKind.Url, start, replaceLen));
        }

        foreach (Match m in EmailRegex.Matches(text))
        {
            if (Overlaps(covered, m.Index, m.Length))
                continue;
            Mark(covered, m.Index, m.Length);
            list.Add(new ContactMatch(ContactKind.Email, m.Index, m.Length));
        }

        foreach (Match m in PhoneCandidateRegex.Matches(text))
        {
            if (!TryNormalizePhone(text, m.Index, m.Length, out var start, out var len))
                continue;
            if (Overlaps(covered, start, len))
                continue;
            Mark(covered, start, len);
            list.Add(new ContactMatch(ContactKind.Phone, start, len));
        }

        // Parenthesised digits first, so the asterisk inside «(*462)» does not split it.
        foreach (Match m in ParenthesizedDigitsRegex.Matches(text))
        {
            if (Overlaps(covered, m.Index, m.Length))
                continue;
            Mark(covered, m.Index, m.Length);
            list.Add(new ContactMatch(ContactKind.Phone, m.Index, m.Length));
        }

        foreach (Match m in ServiceCodeRegex.Matches(text))
        {
            if (Overlaps(covered, m.Index, m.Length))
                continue;
            Mark(covered, m.Index, m.Length);
            list.Add(new ContactMatch(ContactKind.Phone, m.Index, m.Length));
        }

        foreach (Match m in ShortPhoneRegex.Matches(text))
        {
            if (m.Index < lineStart)
                continue;
            if (!TryNormalizeShortPhone(text, m.Index, m.Length, out var start, out var len))
                continue;
            if (Overlaps(covered, start, len))
                continue;
            Mark(covered, start, len);
            list.Add(new ContactMatch(ContactKind.Phone, start, len));
        }

        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        return list;
    }

    /// <summary>
    /// The first thing the stream has to hold from: the contact line's marker, or something
    /// a voice cannot say. Short numbers are not a reason to hold: they only count inside
    /// the contact line, which starts with its marker.
    /// </summary>
    private static bool TryFindNextContact(string text, int start, out ContactMatch match)
    {
        match = default;
        ContactMatch? best = null;

        void Consider(ContactMatch candidate)
        {
            if (candidate.Index < start) return;
            if (best is null || candidate.Index < best.Value.Index)
                best = candidate;
        }

        var marker = ContactLineMarker.Match(text);
        if (marker.Success)
            Consider(new ContactMatch(ContactKind.Marker, marker.Index, marker.Length));

        foreach (Match m in UrlRegex.Matches(text))
        {
            var (s, coreLen) = TrimUrlMatch(text, m.Index, m.Length);
            if (coreLen <= 0) continue;
            if (s > 0 && text[s - 1] == '@') continue;
            if (!IsPlausibleUrl(text.Substring(s, coreLen))) continue;
            Consider(new ContactMatch(ContactKind.Url, s, coreLen));
        }

        foreach (Match m in EmailRegex.Matches(text))
            Consider(new ContactMatch(ContactKind.Email, m.Index, m.Length));

        foreach (Match m in PhoneCandidateRegex.Matches(text))
        {
            if (!TryNormalizePhone(text, m.Index, m.Length, out var s, out var len))
                continue;
            Consider(new ContactMatch(ContactKind.Phone, s, len));
        }

        foreach (Match m in ParenthesizedDigitsRegex.Matches(text))
            Consider(new ContactMatch(ContactKind.Phone, m.Index, m.Length));

        foreach (Match m in ServiceCodeRegex.Matches(text))
            Consider(new ContactMatch(ContactKind.Phone, m.Index, m.Length));

        if (best is null)
            return false;
        match = best.Value;
        return true;
    }

    private static (int Start, int Length) TrimUrlMatch(string text, int index, int length)
    {
        var end = index + length;
        while (end > index && TrailingUrlJunk.Contains(text[end - 1]))
            end--;
        return (index, end - index);
    }

    /// <summary>
    /// Accept http(s)/www as-is; bare hostnames only when the TLD looks real
    /// (avoids matching "Dr. Juan", "p.ej", version numbers, etc.).
    /// </summary>
    private static bool IsPlausibleUrl(string candidate)
    {
        if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var host = candidate.Split('/', 2)[0];
        var parts = host.Split('.');
        if (parts.Length < 2)
            return false;

        var allNumeric = true;
        foreach (var part in parts)
        {
            if (part.Length == 0)
                return false;
            if (!part.All(char.IsDigit))
                allNumeric = false;
        }

        if (allNumeric)
            return false;

        if (KnownTlds.Contains(parts[^1]))
            return true;

        if (parts.Length >= 3)
        {
            var multi = parts[^2] + "." + parts[^1];
            if (KnownMultiTlds.Contains(multi))
                return true;
        }

        return false;
    }

    private static bool TryNormalizePhone(string text, int index, int length, out int start, out int len)
    {
        start = index;
        len = length;
        while (len > 0 && IsPhoneTrimChar(text[start + len - 1]))
            len--;
        while (len > 0 && char.IsWhiteSpace(text[start]))
        {
            start++;
            len--;
        }

        if (len <= 0)
            return false;

        var digits = CountDigits(text.AsSpan(start, len));
        // E.164-ish: 7–15 digits.
        if (digits is < 7 or > 15)
            return false;

        if (start > 0 && char.IsLetter(text[start - 1]))
            return false;
        var after = start + len;
        if (after < text.Length && char.IsLetter(text[after]))
            return false;

        return true;
    }

    private static bool TryNormalizeShortPhone(string text, int index, int length, out int start, out int len)
    {
        start = index;
        len = length;
        while (len > 0 && (text[start + len - 1] is ')' or '.' or ',' or ';' or ':' or '!'))
            len--;
        while (len > 0 && (text[start] is '(' || char.IsWhiteSpace(text[start])))
        {
            start++;
            len--;
        }

        if (len is < 3 or > 6)
            return false;

        for (var i = 0; i < len; i++)
        {
            if (!char.IsDigit(text[start + i]))
                return false;
        }

        // Reject years (1900–2099).
        if (len == 4
            && int.TryParse(text.AsSpan(start, len), out var year)
            && year is >= 1900 and <= 2099)
        {
            return false;
        }

        if (start > 0 && char.IsLetterOrDigit(text[start - 1]))
            return false;
        var after = start + len;
        if (after < text.Length && char.IsLetterOrDigit(text[after]))
            return false;

        // Expand match to include wrapping parentheses when present.
        if (start > 0 && text[start - 1] == '('
            && after < text.Length && text[after] == ')')
        {
            start--;
            len += 2;
        }

        return true;
    }

    private static bool IsPhoneTrimChar(char c) =>
        char.IsWhiteSpace(c) || c is '-' or '.' or '(' or ')';

    private static int LengthWithoutIncompleteTail(string text)
    {
        if (text.Length == 0)
            return 0;

        // Each rule can ask to hold the end of the text back; the earliest start wins.
        var safe = text.Length;

        for (var len = 1; len <= 8 && len <= text.Length; len++)
        {
            var suffix = text[^len..];
            if (IsIncompleteUrlPrefix(suffix))
            {
                safe = Math.Min(safe, text.Length - len);
                break;
            }
        }

        // Incomplete email (local@ or trailing @).
        var at = text.LastIndexOf('@');
        if (at >= 0)
        {
            var afterAt = text[(at + 1)..];
            if (afterAt.Length == 0 || !afterAt.Contains('.'))
            {
                var localStart = at;
                while (localStart > 0 && IsEmailLocalChar(text[localStart - 1]))
                    localStart--;
                safe = Math.Min(safe, localStart);
            }
        }

        var phoneTailStart = FindPhoneIshTailStart(text);
        if (phoneTailStart >= 0)
        {
            var tail = text[phoneTailStart..];
            var digits = CountDigits(tail);
            // Hold growing digit runs; complete short codes are finalized on Flush
            // or when non-phone text follows.
            if (digits > 0 && digits < 7)
                safe = Math.Min(safe, phoneTailStart);
            else if (digits is >= 7 and <= 15 && CouldGrowPhone(tail))
                safe = Math.Min(safe, phoneTailStart);
            else if (tail.TrimStart().StartsWith('+') && digits < 7)
                safe = Math.Min(safe, phoneTailStart);
        }

        // The marker of the contact line may be arriving in pieces («Por favor, ve» + «a los…»).
        var markerTail = MarkerPrefixTailStart(text);
        if (markerTail >= 0)
            safe = Math.Min(safe, markerTail);

        return safe;
    }

    /// <summary>Where the end of the text starts looking like the beginning of the contact-line marker, or -1.</summary>
    private static int MarkerPrefixTailStart(string text)
    {
        var longest = Math.Min(text.Length, MarkerCanonical.Length + 4);
        for (var len = longest; len >= 1; len--)
        {
            // The marker starts a word: the «p» that ends «FALLBACK_CAP» is not the start of it.
            var start = text.Length - len;
            if (start > 0 && char.IsLetterOrDigit(text[start - 1]))
                continue;

            var normalized = NormalizeForMarker(text[^len..]);
            if (normalized.Length > 0 && MarkerCanonical.StartsWith(normalized, StringComparison.Ordinal))
                return text.Length - len;
        }

        return -1;
    }

    // Lowercase, commas dropped, runs of whitespace collapsed: how the marker is compared while it arrives.
    private static string NormalizeForMarker(string s)
    {
        var sb = new StringBuilder(s.Length);
        var previousSpace = false;
        foreach (var ch in s)
        {
            if (ch == ',')
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!previousSpace)
                    sb.Append(' ');
                previousSpace = true;
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
            previousSpace = false;
        }

        return sb.ToString();
    }

    private static bool IsEmailLocalChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '%' or '+' or '-';

    private static bool IsIncompleteUrlPrefix(string s)
    {
        if (s.Length == 0 || s.Length > 8)
            return false;
        var lower = s.ToLowerInvariant();
        return lower is "h" or "ht" or "htt" or "http" or "https"
            or "http:" or "https:" or "http:/" or "https:/"
            or "w" or "ww" or "www" or "www.";
    }

    private static int FindPhoneIshTailStart(string text)
    {
        var i = text.Length - 1;
        while (i >= 0 && IsPhoneSepOrDigit(text[i]))
            i--;
        var start = i + 1;
        if (start >= text.Length)
            return -1;
        if (start > 0 && text[start - 1] == '+')
            start--;
        return start;
    }

    private static bool IsPhoneSepOrDigit(char c) =>
        char.IsDigit(c) || c is ' ' or '-' or '.' or '(' or ')' or '*' or '\t';

    private static bool CouldGrowPhone(string tail)
    {
        var last = tail[^1];
        return last is '-' or '.' or '(' or ' ' || char.IsDigit(last);
    }

    private static int CountDigits(ReadOnlySpan<char> s)
    {
        var n = 0;
        foreach (var c in s)
        {
            if (char.IsDigit(c)) n++;
        }
        return n;
    }

    private static int CountDigits(string s) => CountDigits(s.AsSpan());

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var prevSpace = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }
            sb.Append(c);
            prevSpace = false;
        }
        return sb.ToString().Trim();
    }

    private static void Mark(bool[] covered, int start, int len)
    {
        for (var i = start; i < start + len && i < covered.Length; i++)
            covered[i] = true;
    }

    private static bool Overlaps(bool[] covered, int start, int len)
    {
        for (var i = start; i < start + len && i < covered.Length; i++)
        {
            if (covered[i]) return true;
        }
        return false;
    }

    private enum ContactKind { Url, Phone, Email, Marker }

    private readonly struct ContactMatch
    {
        public ContactMatch(ContactKind kind, int index, int length)
        {
            Kind = kind;
            Index = index;
            Length = length;
        }

        public ContactKind Kind { get; }
        public int Index { get; }
        public int Length { get; }
    }
}
