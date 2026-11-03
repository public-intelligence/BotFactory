using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using SmartReader;

namespace BotFactory.Infrastructure.Services;

public static partial class HtmlContentExtractor
{
    public const int MinUsefulTextLength = 100;

    public static string ExtractPlainText(string url, string html)
    {
        try
        {
            var article = Reader.ParseArticle(url, html);
            if (article.IsReadable && !string.IsNullOrWhiteSpace(article.TextContent))
            {
                var smart = NormalizeWhitespace(article.TextContent);
                if (smart.Length >= MinUsefulTextLength)
                    return smart;
            }
        }
        catch
        {
            // Fall through to landmark extraction.
        }

        return ExtractByLandmarks(html);
    }

    public static bool LooksLikeBotWall(string html)
    {
        var sample = html.Length > 8000 ? html[..8000] : html;
        return BotWallRegex().IsMatch(sample);
    }

    public static bool LooksLikeSpaShell(string html)
    {
        var sample = html.Length > 12000 ? html[..12000] : html;
        if (!SpaShellRegex().IsMatch(sample))
            return false;

        var text = NormalizeWhitespace(TagRegex().Replace(sample, " "));
        return text.Length < MinUsefulTextLength;
    }

    private static string ExtractByLandmarks(string html)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);

        foreach (var node in document.QuerySelectorAll("script, style, noscript, svg, iframe, nav, footer, header, aside"))
            node.Remove();

        var root = document.QuerySelector("main")
            ?? document.QuerySelector("[role='main']")
            ?? document.QuerySelector("article")
            ?? document.Body
            ?? document.DocumentElement;

        if (root is null)
            return string.Empty;

        return NormalizeWhitespace(root.TextContent);
    }

    private static string NormalizeWhitespace(string text)
    {
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(text) ?? text, " ").Trim();
    }

    [GeneratedRegex(@"cloudflare|attention required|radware bot manager captcha|perfdrive|why have i been blocked|sorry, you have been blocked", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BotWallRegex();

    [GeneratedRegex(@"<app-root\b|id=[""']root[""']|id=[""']__nuxt[""']|id=[""']app[""']|ng-version=|__NUXT__|Loading\.\.\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpaShellRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
