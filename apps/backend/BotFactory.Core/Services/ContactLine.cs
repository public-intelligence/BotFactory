using System.Text;
using System.Text.RegularExpressions;

namespace BotFactory.Core.Services;

/// <summary>A contact the chat offers as a button: what it shows and what it opens.</summary>
/// <param name="Kind"><c>phone</c>, <c>url</c> or <c>email</c>.</param>
/// <param name="Label">What the button shows: the number as written, the site's domain, the address.</param>
/// <param name="Href">What it opens: <c>tel:</c>, <c>https://</c> or <c>mailto:</c>.</param>
/// <param name="Detail">A note the answer gave next to it, e.g. «Centro de contacto y WhatsApp».</param>
public sealed record ContactButton(string Kind, string Label, string Href, string? Detail = null);

/// <summary>An answer split for the chat: its text without the contact block, and the block's contacts as buttons.</summary>
public sealed record ContactSplit(string DisplayContent, IReadOnlyList<ContactButton> Contacts);

/// <summary>
/// The contact block the bot closes an answer with — «Por favor, vea los datos de contacto:
/// 809-583-4047 · https://… · …» — read into buttons, so every chat (the site, TainaApp) shows
/// them the same way and the text does not repeat them (OGTIC, 7-Nov feedback, U20: the app
/// showed the line in full and the buttons under it).
/// <para>
/// The block is recognised by its shape, not its words, because the bot writes the label in the
/// visitor's language («Tanpri, gade detay kontak yo:»): the last lines of the answer, each a
/// label ending in «:» followed only by contacts separated by «·» (a note in parentheses is
/// allowed), or a label alone right before such lines. A line that also says anything else, or
/// a contact line in the middle of the answer, stays text. A contact line that closes a longer
/// line («… visitar el portal gob.do. Por favor, vea los datos de contacto: *462 · gob.do») keeps
/// what comes before its label.
/// </para>
/// </summary>
public static class ContactLine
{
    private const int MaxLabelLength = 90;
    private const int MaxLeftover = 30;
    private const int MaxNameLength = 60;

    private static readonly Regex EmailRe = new(
        @"^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UrlRe = new(
        @"(?:https?://[^\s<>""'·]+|www\.[^\s<>""'·]+|\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:do|com|org|net|gov|gob|edu|io|app|info|lat|es|mx)\b(?:/[^\s<>""'·]*)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PhoneRe = new(
        @"(?:\*\d{2,6}|\+?\(?\d[\d \-().]{1,22}\d|\d{3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AnyContactRe = new(
        @"https?://|www\.|@|\d{3}|\b[a-z0-9-]+\.(?:gob|gov|com|org|net)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingNote = new(
        @"\s*\(([^()]{1,80})\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly char[] Separators = ['·', '•'];

    // Between contacts: «·» or «•», and « / » between numbers (+1 212-768-2480 / +1 212-768-2481);
    // a «/» inside a link has no spaces around it.
    private static readonly Regex ItemSeparator = new(
        @"\s*[·•]\s*|\s+/\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// What a chat needs of a stored message: for an answer of the bot that closes with a contact
    /// block, the buttons and the text without the block; nulls for anything else.
    /// </summary>
    public static (IReadOnlyList<ContactButton>? Contacts, string? DisplayContent) ForMessage(string role, string? content)
    {
        if (!string.Equals(role, "Assistant", StringComparison.Ordinal))
            return (null, null);
        var split = Split(content);
        return split.Contacts.Count == 0 ? (null, null) : (split.Contacts, split.DisplayContent);
    }

    /// <summary>The answer without its closing contact block, and that block's contacts; the answer unchanged when it has none.</summary>
    public static ContactSplit Split(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new ContactSplit(content ?? string.Empty, []);

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var end = lines.Length;
        while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
            end--;

        // A closing question after the block («¿Quiere que le dé más detalles?») stays, after the text.
        string? closingQuestion = null;
        if (end > 0 && lines[end - 1].TrimEnd().EndsWith('?') && ReadLine(lines[end - 1]) is null)
        {
            closingQuestion = lines[end - 1].Trim();
            end--;
            while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
                end--;
        }

        var contacts = new List<ContactButton>();
        var keepUntil = end;          // lines [0, keepUntil) stay text
        string? keptPrefix = null;    // sentences before the label on the first line of the block
        var sawItems = false;
        var sawLabeled = false;
        var sawUnlabeled = false;
        var sawHeading = false;

        for (var i = end - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!sawItems)
                    break;
                continue;
            }

            var read = ReadLine(line);
            if (read is null)
                break;

            var (kind, prefix, items) = read.Value;
            // Contacts on a line of their own sit right under a heading, never over a labeled line.
            if (kind == LineKind.Unlabeled && sawLabeled)
                break;
            if (kind == LineKind.Heading)
            {
                // A label alone counts only as the heading of the contact lines under it.
                if (!sawItems)
                    break;
                sawHeading = true;
                keepUntil = i;
                keptPrefix = prefix.Length > 0 ? prefix : null;
                break;
            }

            contacts.InsertRange(0, items);
            sawItems = true;
            sawUnlabeled |= kind == LineKind.Unlabeled;
            sawLabeled |= kind == LineKind.Labeled;
            keepUntil = i;
            if (prefix.Length > 0)
            {
                // «… gob.do. Por favor, vea los datos de contacto: …»: the sentences before stay.
                keptPrefix = prefix;
                break;
            }
        }

        // Contacts on a line of their own («809-583-4047 · gob.do») are a block only under a heading.
        if (!sawItems || (sawUnlabeled && !sawHeading))
            return new ContactSplit(content, []);

        var text = new StringBuilder();
        for (var i = 0; i < keepUntil; i++)
        {
            if (i > 0)
                text.Append('\n');
            text.Append(lines[i]);
        }

        if (keptPrefix is not null)
        {
            if (text.Length > 0)
                text.Append('\n');
            text.Append(keptPrefix);
        }

        if (closingQuestion is not null)
        {
            var body = text.ToString().TrimEnd();
            text.Clear().Append(body);
            if (text.Length > 0)
                text.Append("\n\n");
            text.Append(closingQuestion);
        }

        var unique = new List<ContactButton>();
        foreach (var c in contacts)
        {
            if (!unique.Any(u => string.Equals(u.Href, c.Href, StringComparison.OrdinalIgnoreCase)))
                unique.Add(c);
        }

        return new ContactSplit(text.ToString().TrimEnd(), unique);
    }

    private enum LineKind
    {
        /// <summary>«Por favor, vea los datos de contacto: X · Y»: a label and its contacts.</summary>
        Labeled,

        /// <summary>«X · Y» on a line of its own, under a heading.</summary>
        Unlabeled,

        /// <summary>«Por favor, vea los datos de contacto:» alone, over its contacts.</summary>
        Heading,
    }

    /// <summary>
    /// A line of the contact block, with the sentences before its label (kept as text) and its
    /// contacts; null when the line is not one.
    /// </summary>
    private static (LineKind Kind, string Prefix, List<ContactButton> Items)? ReadLine(string line)
    {
        var trimmed = line.Trim();

        // A label and its contacts: the label ends at a «:» (not the one of «https:») that only
        // contacts follow, and it is the last sentence before it.
        for (var colon = trimmed.LastIndexOf(':'); colon > 0; colon = trimmed.LastIndexOf(':', colon - 1))
        {
            if (colon + 1 < trimmed.Length && trimmed[colon + 1] == '/')
                continue;

            var (prefix, label) = SplitLabel(trimmed[..colon]);
            if (label.Length == 0 || label.Length > MaxLabelLength || AnyContactRe.IsMatch(label)
                || prefix.IndexOfAny(Separators) >= 0)
                continue;

            var tail = trimmed[(colon + 1)..].Trim();
            if (tail.Length == 0)
                return (LineKind.Heading, prefix, []);

            if (ReadItems(tail, allowWords: true) is { } labeled)
                return (LineKind.Labeled, prefix, labeled);
        }

        // Contacts alone on their line: nothing but contacts («Nombre: contacto» at most), or a
        // sentence with a year in it would pass for a phone.
        return ReadItems(trimmed, allowWords: false) is { } items ? (LineKind.Unlabeled, string.Empty, items) : null;
    }

    /// <param name="allowWords">After a label, an item may carry a few words («WhatsApp 809-…»).</param>
    private static List<ContactButton>? ReadItems(string text, bool allowWords)
    {
        var items = new List<ContactButton>();
        foreach (var piece in ItemSeparator.Split(text))
        {
            var raw = piece.Trim();
            if (raw.Length == 0)
                continue;
            var item = ReadItem(raw, allowWords) ?? ReadNamedItem(raw);
            if (item is null)
                return null;
            items.Add(item);
        }

        return items.Count == 0 ? null : items;
    }

    /// <summary>«Ricardo Limardo: https://…» — a contact with a name before it.</summary>
    private static ContactButton? ReadNamedItem(string raw)
    {
        var colon = raw.IndexOf(": ", StringComparison.Ordinal);
        if (colon <= 0 || colon > MaxNameLength)
            return null;
        var name = raw[..colon].Trim();
        if (AnyContactRe.IsMatch(name) || ReadItem(raw[(colon + 2)..], allowWords: false) is not { } item)
            return null;
        return item with { Detail = item.Detail is null ? name : $"{name} ({item.Detail})" };
    }

    /// <summary>The label is the last sentence before the «:»; earlier sentences are the prefix.</summary>
    private static (string Prefix, string Label) SplitLabel(string head)
    {
        var cut = -1;
        for (var i = head.Length - 2; i >= 0; i--)
        {
            if (head[i] is '.' or '!' or '?' && char.IsWhiteSpace(head[i + 1]))
            {
                cut = i + 1;
                break;
            }
        }

        return cut < 0
            ? (string.Empty, head.Trim())
            : (head[..cut].Trim(), head[cut..].Trim());
    }

    private static ContactButton? ReadItem(string raw, bool allowWords)
    {
        var text = raw.Trim().TrimEnd('.', ';', ',');
        string? detail = null;
        var note = TrailingNote.Match(text);
        if (note.Success)
        {
            detail = note.Groups[1].Value.Trim();
            text = text[..note.Index].Trim();
        }

        if (EmailRe.IsMatch(text))
            return new ContactButton("email", text, "mailto:" + text, detail);

        var url = UrlRe.Match(text);
        if (url.Success && Leftover(text, url, allowWords) is { } urlLead)
        {
            var link = url.Value.TrimEnd('.', ',', ';', ')');
            return new ContactButton("url", UrlLabel(link), UrlHref(link), Join(urlLead, detail));
        }

        var phone = PhoneRe.Match(text);
        if (phone.Success && Leftover(text, phone, allowWords) is { } phoneLead)
        {
            var number = phone.Value.Trim();
            var digits = new string(number.Where(char.IsDigit).ToArray());
            var isCode = number.StartsWith('*');
            if (isCode ? digits.Length is < 2 or > 6 : digits.Length is < 3 or > 15)
                return null;
            var href = isCode ? "tel:*" + digits : number.StartsWith('+') ? "tel:+" + digits : "tel:" + digits;
            return new ContactButton("phone", number, href, Join(phoneLead, detail));
        }

        return null;
    }

    /// <summary>
    /// What is left of the item besides its contact: a few words without digits («WhatsApp»)
    /// are a note; anything longer means the item is not a plain contact.
    /// </summary>
    private static string? Leftover(string text, Match contact, bool allowWords)
    {
        var rest = (text[..contact.Index] + " " + text[(contact.Index + contact.Length)..]).Trim().TrimEnd(':').Trim();
        if (rest.Length == 0)
            return string.Empty;
        return allowWords && rest.Length <= MaxLeftover && !rest.Any(char.IsDigit) ? rest : null;
    }

    private static string? Join(string lead, string? detail) =>
        (lead.Length, detail) switch
        {
            (0, null) => null,
            (0, _) => detail,
            (_, null) => lead,
            _ => $"{lead} ({detail})",
        };

    private static string UrlHref(string link)
    {
        var href = link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? link : "https://" + link;
        // gob.do without «www» does not resolve: the portal is www.gob.do.
        return Regex.Replace(href, @"^(https?://)gob\.do(?=/|$)", "$1www.gob.do", RegexOptions.IgnoreCase);
    }

    private static string UrlLabel(string link)
    {
        if (!Uri.TryCreate(UrlHref(link), UriKind.Absolute, out var uri))
            return link;
        var host = uri.Host.ToLowerInvariant();
        if ((host.StartsWith("google.", StringComparison.Ordinal) || host.StartsWith("www.google.", StringComparison.Ordinal))
            && uri.AbsolutePath.StartsWith("/maps", StringComparison.OrdinalIgnoreCase)
            || host == "maps.google.com" || host == "maps.app.goo.gl")
            return "Ver en el mapa";
        if (uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return "Ver el PDF";
        return host == "www.gob.do" ? host : host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
