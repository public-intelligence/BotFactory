using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BotFactory.Core.Services;

/// <summary>
/// A tool call that Gemini wrote as text instead of making it. Its function calling is
/// represented internally as Python-like code, and now and then that code comes out as the
/// answer: <c>default_api.lugares(que='salud', lugar='Santiago', tipo='hospital')</c>
/// (TAINA, conversations 1994 and 2396, a question in Haitian Creole), sometimes inside a
/// <c>```tool_code</c> block or a <c>print(…)</c>. The text names the tool and its arguments,
/// so it can be run as the call the model meant; it must never reach the visitor.
/// </summary>
public static class WrittenToolCall
{
    public enum HeadDecision
    {
        /// <summary>Too short to tell: keep holding the text.</summary>
        Undecided,

        /// <summary>It starts like a written call: hold the whole pass and read it at the end.</summary>
        Hold,

        /// <summary>It is ordinary text: deliver it.</summary>
        Release,
    }

    public enum Kind
    {
        /// <summary>Ordinary text.</summary>
        NotACall,

        /// <summary>One or more written calls, read into names and arguments.</summary>
        Calls,

        /// <summary>A written call whose arguments cannot be read: never shown, never run.</summary>
        Unreadable,
    }

    public sealed record Call(string Name, JsonElement Args);

    public sealed record Parsed(Kind Kind, IReadOnlyList<Call> Calls)
    {
        public static readonly Parsed NotACall = new(Kind.NotACall, []);
        public static readonly Parsed Unreadable = new(Kind.Unreadable, []);
    }

    private const string ApiPrefix = "default_api.";

    // How a written call can start, besides «<tool>(» and «print(<tool>(» for the bot's own tools.
    private static readonly string[] FixedStarts = [ApiPrefix, "print(" + ApiPrefix, "```tool_code", "tool_code"];

    private static readonly Regex CallLine = new(
        @"^(?<api>default_api\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<args>.*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>
    /// What to do with the first characters of a pass: hold them while they could still be the
    /// start of a written call, release them as soon as they cannot.
    /// </summary>
    public static HeadDecision ClassifyHead(string head, IReadOnlyCollection<string> toolNames)
    {
        var h = head.TrimStart();
        if (h.Length == 0)
            return HeadDecision.Undecided;

        var undecided = false;
        foreach (var start in Starts(toolNames))
        {
            if (h.StartsWith(start, StringComparison.Ordinal))
                return HeadDecision.Hold;
            if (start.StartsWith(h, StringComparison.Ordinal))
                undecided = true;
        }

        return undecided ? HeadDecision.Undecided : HeadDecision.Release;
    }

    /// <summary>
    /// Reads a whole pass of text: written calls (one per line), a written call that cannot be
    /// read, or ordinary text. Without the <c>default_api.</c> prefix, only the bot's own tool
    /// names count as a call.
    /// </summary>
    public static Parsed Parse(string text, IReadOnlyCollection<string> toolNames)
    {
        var body = StripWrappers(text);
        if (body.Length == 0)
            return LooksLikeCall(text, toolNames) ? Parsed.Unreadable : Parsed.NotACall;

        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var calls = new List<Call>();
        foreach (var line in lines)
        {
            var call = ReadLine(line, toolNames, out var isCallShaped);
            if (call is null)
                return isCallShaped || LooksLikeCall(text, toolNames) ? Parsed.Unreadable : Parsed.NotACall;
            calls.Add(call);
        }

        return new Parsed(Kind.Calls, calls);
    }

    private static IEnumerable<string> Starts(IReadOnlyCollection<string> toolNames)
    {
        foreach (var start in FixedStarts)
            yield return start;
        foreach (var name in toolNames)
        {
            yield return name + "(";
            yield return "print(" + name + "(";
        }
    }

    private static bool LooksLikeCall(string text, IReadOnlyCollection<string> toolNames) =>
        ClassifyHead(text, toolNames) == HeadDecision.Hold;

    /// <summary>The text without code fences and a bare <c>tool_code</c> label.</summary>
    private static string StripWrappers(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = t.IndexOf('\n');
            t = newline < 0 ? "" : t[(newline + 1)..];
        }

        t = t.Trim();
        if (t.EndsWith("```", StringComparison.Ordinal))
            t = t[..^3];

        t = t.Trim();
        if (t.StartsWith("tool_code", StringComparison.Ordinal))
            t = t["tool_code".Length..];

        return t.Trim();
    }

    private static Call? ReadLine(string line, IReadOnlyCollection<string> toolNames, out bool isCallShaped)
    {
        isCallShaped = false;
        var l = line.Trim().TrimEnd(';').Trim();
        if (l.StartsWith("print(", StringComparison.Ordinal) && l.EndsWith(')'))
            l = l["print(".Length..^1].Trim();

        var m = CallLine.Match(l);
        if (!m.Success)
            return null;

        var name = m.Groups["name"].Value;
        var hasApi = m.Groups["api"].Success;
        if (!hasApi && !toolNames.Contains(name))
            return null;

        isCallShaped = true;
        return TryReadArgs(m.Groups["args"].Value, out var args) ? new Call(name, args) : null;
    }

    /// <summary>
    /// Keyword arguments in Python form (<c>que='salud', n=3, activo=True</c>) as a JSON object.
    /// Strings, numbers, booleans and None only; anything else (positional arguments, lists)
    /// is not read.
    /// </summary>
    private static bool TryReadArgs(string source, out JsonElement args)
    {
        args = default;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var i = 0;
            SkipSpaces(source, ref i);
            while (i < source.Length)
            {
                var keyStart = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                    i++;
                if (i == keyStart)
                    return false;
                var key = source[keyStart..i];

                SkipSpaces(source, ref i);
                if (i >= source.Length || source[i] != '=')
                    return false;
                i++;
                SkipSpaces(source, ref i);

                writer.WritePropertyName(key);
                if (!TryWriteValue(source, ref i, writer))
                    return false;

                SkipSpaces(source, ref i);
                if (i < source.Length)
                {
                    if (source[i] != ',')
                        return false;
                    i++;
                    SkipSpaces(source, ref i);
                }
            }

            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        args = doc.RootElement.Clone();
        return true;
    }

    private static bool TryWriteValue(string s, ref int i, Utf8JsonWriter writer)
    {
        if (i >= s.Length)
            return false;

        var c = s[i];
        if (c is '\'' or '"')
        {
            var text = new StringBuilder();
            i++;
            while (i < s.Length && s[i] != c)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    text.Append(s[i] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => s[i],
                    });
                }
                else
                {
                    text.Append(s[i]);
                }

                i++;
            }

            if (i >= s.Length)
                return false;
            i++;
            writer.WriteStringValue(text.ToString());
            return true;
        }

        var start = i;
        while (i < s.Length && s[i] != ',' && !char.IsWhiteSpace(s[i]))
            i++;
        var token = s[start..i];
        switch (token)
        {
            case "True" or "true":
                writer.WriteBooleanValue(true);
                return true;
            case "False" or "false":
                writer.WriteBooleanValue(false);
                return true;
            case "None" or "null":
                writer.WriteNullValue();
                return true;
        }

        if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
        {
            writer.WriteNumberValue(whole);
            return true;
        }

        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            writer.WriteNumberValue(real);
            return true;
        }

        return false;
    }

    private static void SkipSpaces(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }
}
