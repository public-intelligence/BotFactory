using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BotFactory.Core.Services;

/// <summary>A datum the answer carried that none of the bot's sources contains.</summary>
public sealed record BlockedDatum(string Kind, string Value, string Sentence);

/// <summary>
/// Keeps a "Respaldo obligatorio" bot from stating data it was not given. The answer is
/// held sentence by sentence; every concrete datum in a sentence — phone, service code,
/// link, e-mail, amount of money (in digits or in words, since the platform asks for
/// amounts in words), any other number of three or more digits — must appear in the
/// evidence (the bot's prompt, what the user said, the results of its tools). A sentence
/// with an unsupported datum is not delivered; in a contact line ("… · … · …") only the
/// unsupported items are dropped. If anything was dropped, the bot's own "no confirmed
/// information" note closes the answer.
///
/// Deterministic on purpose: it checks data, not meaning. Prose without data (a wrong
/// claim in words) is outside its reach.
/// </summary>
public sealed class GroundingGate
{
    // A sentence ends at a line break, or at . ! ? followed by space and something that
    // starts a sentence. "a. m. de lunes" and "1.50" are not boundaries.
    private static readonly Regex Boundary = new(
        @"\n+|(?<=[.!?])[ \t]+(?=[A-ZÁÉÍÓÚÑ¿¡""“•(\-\d])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _note;
    private readonly GroundingEvidence _evidence = new();
    private readonly StringBuilder _pending = new();
    private readonly List<BlockedDatum> _blocked = [];
    private bool _discarded;

    /// <param name="note">Said once at the end when something was dropped (the bot's
    /// grounded-only message, which is configuration and is not checked).</param>
    public GroundingGate(string note)
    {
        _note = note;
    }

    public IReadOnlyList<BlockedDatum> Blocked => _blocked;

    public void AddEvidence(string? text) => _evidence.Add(text);

    /// <summary>Feeds streamed text; returns what is safe to deliver now.</summary>
    public List<string> Push(string delta)
    {
        var ready = new List<string>();
        if (_discarded || string.IsNullOrEmpty(delta))
            return ready;

        _pending.Append(delta);
        var text = _pending.ToString();
        var consumed = 0;
        foreach (Match m in Boundary.Matches(text))
        {
            var end = m.Index + m.Length;
            var sentence = text[consumed..end];
            consumed = end;
            if (Verify(sentence) is { } approved)
                ready.Add(approved);
        }

        if (consumed > 0)
            _pending.Remove(0, consumed);
        return ready;
    }

    /// <summary>End of the answer: the last sentence, then the note if anything was dropped.</summary>
    public List<string> Flush()
    {
        var ready = new List<string>();
        if (_discarded)
            return ready;

        if (_pending.Length > 0)
        {
            if (Verify(_pending.ToString()) is { } approved)
                ready.Add(approved);
            _pending.Clear();
        }

        if (_blocked.Count > 0 && !string.IsNullOrWhiteSpace(_note))
            ready.Add("\n\n" + _note.Trim());
        return ready;
    }

    /// <summary>The turn ended another way (e.g. the grounded-only reply); drop what is held.</summary>
    public void Discard()
    {
        _discarded = true;
        _pending.Clear();
    }

    /// <summary>The sentence to deliver (possibly with contact items removed), or null to drop it.</summary>
    private string? Verify(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return sentence;

        if (sentence.Contains('·'))
            return VerifyContactLine(sentence);

        var unsupported = _evidence.FindUnsupported(sentence);
        if (unsupported.Count == 0)
            return sentence;

        foreach (var (kind, value) in unsupported)
            _blocked.Add(new BlockedDatum(kind, value, Excerpt(sentence)));
        return null;
    }

    private string? VerifyContactLine(string line)
    {
        var trailing = line[line.TrimEnd().Length..];
        var body = line.TrimEnd();
        var items = body.Split('·');

        // "Por favor, vea los datos de contacto: 809-… " → prefix + first item.
        var prefix = string.Empty;
        var first = items[0];
        var colon = first.LastIndexOf(": ", StringComparison.Ordinal);
        if (colon >= 0)
        {
            prefix = first[..(colon + 2)];
            items[0] = first[(colon + 2)..];
        }

        if (_evidence.FindUnsupported(prefix) is { Count: > 0 } prefixIssues)
        {
            foreach (var (kind, value) in prefixIssues)
                _blocked.Add(new BlockedDatum(kind, value, Excerpt(line)));
            return null;
        }

        var kept = new List<string>();
        foreach (var raw in items)
        {
            var item = raw.Trim();
            if (item.Length == 0)
                continue;
            var issues = _evidence.FindUnsupported(item);
            if (issues.Count == 0)
            {
                kept.Add(item);
                continue;
            }

            foreach (var (kind, value) in issues)
                _blocked.Add(new BlockedDatum(kind, value, Excerpt(line)));
        }

        if (kept.Count == 0)
            return null;
        return prefix + string.Join(" · ", kept) + trailing;
    }

    private static string Excerpt(string sentence)
    {
        var s = sentence.Trim();
        return s.Length <= 200 ? s : s[..200] + "…";
    }
}

/// <summary>
/// What the bot's sources say, indexed for the checks: digit runs (phones, numbers),
/// numeric values (amounts in digits or Spanish words), links and e-mails.
/// </summary>
public sealed class GroundingEvidence
{
    private static readonly Regex EmailRe = new(
        @"[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Scheme, www., or a bare host whose last label is a common TLD (gob.do, gov.do, …).
    private static readonly Regex UrlRe = new(
        @"https?://[^\s<>""'·]+|www\.[^\s<>""'·]+|\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:do|com|org|net|gov|gob|edu|io|app|info|lat|es|mx|cr)\b(?:/[^\s<>""'·]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PhoneRe = new(
        @"(?<![\w*])\+?\d[\d \-().]{5,22}\d(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A phone said digit by digit, as voice-friendly answers write it:
    // "ocho, cero, nueve; cinco, dos, tres; cero, cuatro, cero, cuatro".
    private const string DigitWord = "cero|uno|dos|tres|cuatro|cinco|seis|siete|ocho|nueve";

    private static readonly Regex SpokenDigitsRe = new(
        @"\b(?:" + DigitWord + @")(?:[\s,;]+(?:" + DigitWord + @")){6,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex StarCodeRe = new(
        @"(?<![\w*])\*\d{3,5}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string Currency = @"(?:pesos?|centavos?|d[óo]lares?|colones?|euros?)";

    private static readonly Regex MoneyDigitsRe = new(
        @"(?:RD\$|US\$|\$)\s?(\d[\d.,]*\d|\d)|(\d[\d.,]*\d|\d)\s?" + Currency + @"\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NumberRe = new(
        @"\d[\d.,]*\d|\d",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MoneyWordsRe = new(
        @"\b((?:" + SpanishNumbers.WordPattern + @")(?:\s+(?:y\s+)?(?:" + SpanishNumbers.WordPattern + @"))*)\s+" + Currency + @"\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WordRunRe = new(
        @"\b(?:" + SpanishNumbers.WordPattern + @")(?:\s+(?:y\s+)?(?:" + SpanishNumbers.WordPattern + @"))*\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly StringBuilder _text = new();
    private string? _textSnapshot;
    private readonly HashSet<string> _digitRuns = new(StringComparer.Ordinal);
    private readonly HashSet<decimal> _values = [];
    private readonly HashSet<string> _emails = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _urls = [];

    public void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _text.Append('\n').Append(text.ToLowerInvariant());
        _textSnapshot = null;

        foreach (Match m in EmailRe.Matches(text))
            _emails.Add(m.Value);
        foreach (Match m in UrlRe.Matches(text))
            _urls.Add(NormalizeUrl(m.Value));
        foreach (Match m in PhoneRe.Matches(text))
        {
            var digits = Digits(m.Value);
            if (digits.Length >= 7)
                _digitRuns.Add(digits);
        }

        foreach (Match m in NumberRe.Matches(text))
        {
            _digitRuns.Add(Digits(m.Value));
            foreach (var v in NumberCandidates(m.Value))
                _values.Add(v);
            // "0.50" also yields 0 and 50: "cincuenta centavos" must find it.
            foreach (var part in m.Value.Split('.', ','))
            {
                if (part.Length > 0 && decimal.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var p))
                    _values.Add(p);
            }
        }

        foreach (Match m in WordRunRe.Matches(text))
        {
            if (SpanishNumbers.TryParse(m.Value, out var v))
                _values.Add(v);
        }
    }

    private string Text => _textSnapshot ??= _text.ToString();

    /// <summary>The data in <paramref name="text"/> that the evidence does not contain.</summary>
    public List<(string Kind, string Value)> FindUnsupported(string text)
    {
        var issues = new List<(string, string)>();
        var covered = new List<(int Start, int End)>();

        bool Covered(Match m) => covered.Any(c => m.Index < c.End && m.Index + m.Length > c.Start);
        void Cover(Match m) => covered.Add((m.Index, m.Index + m.Length));

        foreach (Match m in EmailRe.Matches(text))
        {
            Cover(m);
            if (!_emails.Contains(m.Value) && !Text.Contains(m.Value.ToLowerInvariant(), StringComparison.Ordinal))
                issues.Add(("email", m.Value));
        }

        foreach (Match m in UrlRe.Matches(text))
        {
            if (Covered(m))
                continue;
            Cover(m);
            if (!UrlSupported(m.Value))
                issues.Add(("url", TrimUrl(m.Value)));
        }

        foreach (Match m in StarCodeRe.Matches(text))
        {
            if (Covered(m))
                continue;
            Cover(m);
            if (!Text.Contains(m.Value, StringComparison.Ordinal) && !_digitRuns.Contains(Digits(m.Value)))
                issues.Add(("phone", m.Value));
        }

        foreach (Match m in PhoneRe.Matches(text))
        {
            if (Covered(m))
                continue;
            var digits = Digits(m.Value);
            if (digits.Length < 7 || digits.Length > 15)
                continue;
            Cover(m);
            if (!PhoneSupported(digits))
                issues.Add(("phone", m.Value.Trim()));
        }

        foreach (Match m in SpokenDigitsRe.Matches(text))
        {
            if (Covered(m))
                continue;
            Cover(m);
            var digits = SpokenDigits(m.Value);
            if (!PhoneSupported(digits))
                issues.Add(("phone", m.Value.Trim()));
        }

        foreach (Match m in MoneyDigitsRe.Matches(text))
        {
            if (Covered(m))
                continue;
            Cover(m);
            var number = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var cents = m.Value.Contains("centavo", StringComparison.OrdinalIgnoreCase);
            if (!ValueSupported(NumberCandidates(number), cents))
                issues.Add(("amount", m.Value.Trim()));
        }

        foreach (Match m in MoneyWordsRe.Matches(text))
        {
            if (Covered(m))
                continue;
            Cover(m);
            if (!SpanishNumbers.TryParse(m.Groups[1].Value, out var value))
                continue;
            var cents = m.Value.Contains("centavo", StringComparison.OrdinalIgnoreCase);
            if (!ValueSupported([value], cents))
                issues.Add(("amount", m.Value.Trim()));
        }

        foreach (Match m in NumberRe.Matches(text))
        {
            if (Covered(m))
                continue;
            var digits = Digits(m.Value);
            if (digits.Length < 3)
                continue;
            if (!_digitRuns.Contains(digits) && !ValueSupported(NumberCandidates(m.Value), cents: false))
                issues.Add(("number", m.Value));
        }

        return issues;
    }

    private bool PhoneSupported(string digits)
    {
        if (_digitRuns.Contains(digits))
            return true;
        // Same number with or without the country code (+1 809…, 809…).
        foreach (var run in _digitRuns)
        {
            if (run.Length < 7)
                continue;
            if (run.EndsWith(digits, StringComparison.Ordinal) || digits.EndsWith(run, StringComparison.Ordinal))
            {
                var shorter = Math.Min(run.Length, digits.Length);
                if (shorter >= 7)
                    return true;
            }
        }

        return false;
    }

    private bool ValueSupported(IEnumerable<decimal> candidates, bool cents)
    {
        foreach (var v in candidates)
        {
            if (_values.Contains(Round(v)))
                return true;
            // "veinte centavos" may be written 0.20 in the source.
            if (cents && _values.Contains(Round(v / 100m)))
                return true;
        }

        return false;
    }

    private bool UrlSupported(string raw)
    {
        var url = NormalizeUrl(raw);
        if (url.Length == 0)
            return true;
        var host = url.Split('/')[0];
        var hasPath = url.Length > host.Length;
        foreach (var known in _urls)
        {
            if (known == url || known.StartsWith(url + "/", StringComparison.Ordinal))
                return true;
            if (!hasPath && known.Split('/')[0] == host)
                return true;
        }

        return Text.Contains(url, StringComparison.Ordinal);
    }

    private static string SpokenDigits(string words)
    {
        var sb = new StringBuilder();
        foreach (var w in Regex.Split(words.ToLowerInvariant(), @"[\s,;]+"))
        {
            var d = w switch
            {
                "cero" => '0', "uno" => '1', "dos" => '2', "tres" => '3', "cuatro" => '4',
                "cinco" => '5', "seis" => '6', "siete" => '7', "ocho" => '8', "nueve" => '9',
                _ => '\0',
            };
            if (d != '\0')
                sb.Append(d);
        }

        return sb.ToString();
    }

    private static string TrimUrl(string raw) => raw.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"', '»');

    private static string NormalizeUrl(string raw)
    {
        var s = TrimUrl(raw.Trim()).ToLowerInvariant();
        if (s.StartsWith("https://", StringComparison.Ordinal)) s = s[8..];
        else if (s.StartsWith("http://", StringComparison.Ordinal)) s = s[7..];
        if (s.StartsWith("www.", StringComparison.Ordinal)) s = s[4..];
        return s.TrimEnd('/');
    }

    private static string Digits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is >= '0' and <= '9')
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static decimal Round(decimal v) => decimal.Round(v, 2);

    /// <summary>
    /// Values a written number can mean: "1,500.00" and "1.500,00" are 1500; "1.500" or
    /// "6,700" may be thousands or a decimal, so both readings count.
    /// </summary>
    public static IEnumerable<decimal> NumberCandidates(string token)
    {
        var t = token.Trim().TrimEnd('.', ',');
        if (t.Length == 0)
            yield break;

        var hasComma = t.Contains(',');
        var hasDot = t.Contains('.');
        if (!hasComma && !hasDot)
        {
            if (decimal.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
                yield return Round(whole);
            yield break;
        }

        if (hasComma && hasDot)
        {
            var lastSep = Math.Max(t.LastIndexOf(','), t.LastIndexOf('.'));
            var intPart = Digits(t[..lastSep]);
            var frac = Digits(t[(lastSep + 1)..]);
            if (decimal.TryParse($"{intPart}.{frac}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mixed))
                yield return Round(mixed);
            yield break;
        }

        var sep = hasComma ? ',' : '.';
        var groups = t.Split(sep);
        var thousands = groups.Length > 1 && groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(g => g.Length == 3);
        if (thousands && decimal.TryParse(string.Concat(groups), NumberStyles.None, CultureInfo.InvariantCulture, out var asThousands))
            yield return Round(asThousands);
        if (groups.Length == 2
            && decimal.TryParse($"{groups[0]}.{groups[1]}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var asDecimal))
            yield return Round(asDecimal);
    }
}

/// <summary>Spanish number words ("seis mil setecientos", "veintidós") to values.</summary>
public static class SpanishNumbers
{
    private static readonly Dictionary<string, long> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cero"] = 0, ["un"] = 1, ["uno"] = 1, ["una"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4,
        ["cinco"] = 5, ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10,
        ["once"] = 11, ["doce"] = 12, ["trece"] = 13, ["catorce"] = 14, ["quince"] = 15,
        ["dieciséis"] = 16, ["dieciseis"] = 16, ["diecisiete"] = 17, ["dieciocho"] = 18, ["diecinueve"] = 19,
        ["veinte"] = 20, ["veintiún"] = 21, ["veintiun"] = 21, ["veintiuno"] = 21, ["veintiuna"] = 21,
        ["veintidós"] = 22, ["veintidos"] = 22, ["veintitrés"] = 23, ["veintitres"] = 23,
        ["veinticuatro"] = 24, ["veinticinco"] = 25, ["veintiséis"] = 26, ["veintiseis"] = 26,
        ["veintisiete"] = 27, ["veintiocho"] = 28, ["veintinueve"] = 29,
        ["treinta"] = 30, ["cuarenta"] = 40, ["cincuenta"] = 50, ["sesenta"] = 60, ["setenta"] = 70,
        ["ochenta"] = 80, ["noventa"] = 90,
        ["cien"] = 100, ["ciento"] = 100, ["doscientos"] = 200, ["doscientas"] = 200,
        ["trescientos"] = 300, ["trescientas"] = 300, ["cuatrocientos"] = 400, ["cuatrocientas"] = 400,
        ["quinientos"] = 500, ["quinientas"] = 500, ["seiscientos"] = 600, ["seiscientas"] = 600,
        ["setecientos"] = 700, ["setecientas"] = 700, ["ochocientos"] = 800, ["ochocientas"] = 800,
        ["novecientos"] = 900, ["novecientas"] = 900,
    };

    /// <summary>Alternation of every number word, longest first so "veintidós" wins over "veinte".</summary>
    public static readonly string WordPattern = string.Join(
        "|",
        Units.Keys
            .Concat(["mil", "millón", "millon", "millones"])
            .OrderByDescending(k => k.Length)
            .Select(Regex.Escape));

    public static bool TryParse(string words, out decimal value)
    {
        value = 0;
        long total = 0;
        long current = 0;
        var any = false;

        foreach (var raw in words.Split((char[])[' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var w = raw.Trim().ToLowerInvariant();
            if (w == "y")
                continue;
            if (Units.TryGetValue(w, out var n))
            {
                current += n;
                any = true;
            }
            else if (w == "mil")
            {
                current = (current == 0 ? 1 : current) * 1_000;
                total += current;
                current = 0;
                any = true;
            }
            else if (w is "millón" or "millon" or "millones")
            {
                total = (total + (current == 0 ? 1 : current)) * 1_000_000;
                current = 0;
                any = true;
            }
            else
            {
                return false;
            }
        }

        if (!any)
            return false;
        value = total + current;
        return true;
    }
}
