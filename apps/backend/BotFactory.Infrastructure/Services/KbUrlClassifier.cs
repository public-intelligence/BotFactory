namespace BotFactory.Infrastructure.Services;

public static class KbUrlClassifier
{
    public const string PdfNotSupportedMessage =
        "Esta URL apunta a un archivo PDF. La ingesta por URL solo admite páginas web (HTML). Descargue el documento y súbalo como archivo .txt.";

    public static bool IsPdfUrl(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return true;

        // Some portals use .pdf in the last segment without a clean extension.
        var lastSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        return lastSegment.Contains(".pdf", StringComparison.OrdinalIgnoreCase);
    }
}
