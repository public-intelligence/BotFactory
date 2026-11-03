using BotFactory.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Services;

public class UrlContentFetcher : IUrlContentFetcher
{
    private readonly IKbScraperClient _scraper;
    private readonly KbScraperOptions _options;
    private readonly ILogger<UrlContentFetcher> _logger;

    public UrlContentFetcher(
        IKbScraperClient scraper,
        IOptions<KbScraperOptions> options,
        ILogger<UrlContentFetcher> logger)
    {
        _scraper = scraper;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UrlContentFetchResult> FetchPlainTextAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new UrlContentFetchResult(false, null, "La URL no es válida.");

        var (safe, safetyError) = await KbUrlValidator.IsSafeToFetchAsync(uri, ct);
        if (!safe)
            return new UrlContentFetchResult(false, null, safetyError);

        if (KbUrlClassifier.IsPdfUrl(uri))
            return new UrlContentFetchResult(false, null, KbUrlClassifier.PdfNotSupportedMessage);

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return new UrlContentFetchResult(
                false,
                null,
                "El servicio de renderizado no está configurado.");
        }

        var render = await _scraper.RenderAsync(url, ct);
        if (!render.Success || string.IsNullOrWhiteSpace(render.Html))
        {
            var error = string.IsNullOrWhiteSpace(render.Error)
                ? "No se pudo obtener contenido de la URL."
                : render.Error!;
            _logger.LogWarning("URL render failed for {Url}: {Error}", url, error);
            return new UrlContentFetchResult(false, null, error, Transient: render.Transient);
        }

        if (render.StatusCode is >= 400 and not 0)
        {
            var hint = render.StatusCode is 401 or 403
                ? " El sitio puede exigir inicio de sesión o bloquear acceso automático."
                : render.StatusCode is 404
                    ? " Verifica que la dirección sea correcta."
                    : " Intenta de nuevo más tarde o usa una URL alternativa.";
            return new UrlContentFetchResult(
                false,
                null,
                $"El servidor respondió HTTP {render.StatusCode}.{hint}");
        }

        var html = render.Html;
        if (string.IsNullOrWhiteSpace(html))
            return new UrlContentFetchResult(false, null, "La respuesta del servidor está vacía.");

        if (HtmlContentExtractor.LooksLikeBotWall(html))
        {
            return new UrlContentFetchResult(
                false,
                null,
                "El sitio bloqueó la descarga (protección anti-bots o captcha). Prueba otra página pública o sube el contenido como archivo .txt.");
        }

        if (HtmlContentExtractor.LooksLikeSpaShell(html))
        {
            return new UrlContentFetchResult(
                false,
                null,
                "La página parece una aplicación JavaScript sin contenido usable. Usa una URL con texto HTML estático o sube un .txt.");
        }

        var plainText = HtmlContentExtractor.ExtractPlainText(url, html);
        if (string.IsNullOrWhiteSpace(plainText) || plainText.Length < HtmlContentExtractor.MinUsefulTextLength)
        {
            return new UrlContentFetchResult(
                false,
                null,
                "No se encontró texto suficiente para indexar. Comprueba que la página tenga contenido visible o sube un archivo .txt.");
        }

        return new UrlContentFetchResult(true, plainText, null);
    }
}
