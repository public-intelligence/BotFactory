using BotFactory.Core.Services;
using BotFactory.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BotFactory.Tests;

public class UrlContentFetcherTests
{
    [Fact]
    public void ExtractPlainText_UsesArticleBodyFromHtml()
    {
        var html = """
            <html>
              <head><title>Beneficio social</title></head>
              <body>
                <nav>Inicio Servicios Contacto</nav>
                <main>
                  <h1>Atención de situaciones de violencia</h1>
                  <p>Aporte económico dirigido a personas víctimas de violencia intrafamiliar, de género, entre otras, para las condiciones necesarias de su protección. Requisitos adicionales se publican en esta página con detalle suficiente para prueba de extracción.</p>
                  <p>También incluye orientación sobre cómo solicitar el beneficio y dónde acudir.</p>
                </main>
                <footer>Derechos reservados</footer>
              </body>
            </html>
            """;

        var text = HtmlContentExtractor.ExtractPlainText("https://example.go.cr/beneficio", html);

        Assert.Contains("violencia", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Derechos reservados", text);
        Assert.True(text.Length >= HtmlContentExtractor.MinUsefulTextLength);
    }

    [Fact]
    public void ExtractPlainText_FallsBackWhenNoReadableArticle()
    {
        var html = """
            <html><body>
              <div id="content" role="main">
                <h1>Consultas</h1>
                <p>Este servicio permite consultar estado de trámites ciudadanos con información oficial pública y enlaces a formularios relacionados presentes en la página principal del portal institucional.</p>
              </div>
            </body></html>
            """;

        var text = HtmlContentExtractor.ExtractPlainText("https://example.go.cr/consultas", html);
        Assert.Contains("trámites", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(text.Length >= 40);
    }

    [Fact]
    public async Task FetchPlainTextAsync_RejectsPdfUrlsWithoutCallingScraper()
    {
        var scraper = new FakeKbScraperClient();
        var fetcher = new UrlContentFetcher(
            scraper,
            Options.Create(new KbScraperOptions { Enabled = true, BaseUrl = "http://localhost:5080" }),
            NullLogger<UrlContentFetcher>.Instance);

        var result = await fetcher.FetchPlainTextAsync(
            "https://www.mtss.go.cr/temas-laborales/salarios/lista.pdf");

        Assert.False(result.Success);
        Assert.Equal(KbUrlClassifier.PdfNotSupportedMessage, result.Error);
        Assert.False(scraper.WasCalled);
    }

    [Fact]
    public async Task FetchPlainTextAsync_UsesScraperHtml()
    {
        var html = """
            <html><body><main>
              <h1>Portal</h1>
              <p>Contenido oficial extenso sobre trámites y servicios ciudadanos publicados en el portal institucional para consulta pública permanente.</p>
            </main></body></html>
            """;
        var scraper = new FakeKbScraperClient { Html = html };
        var fetcher = new UrlContentFetcher(
            scraper,
            Options.Create(new KbScraperOptions { Enabled = true, BaseUrl = "http://localhost:5080" }),
            NullLogger<UrlContentFetcher>.Instance);

        var result = await fetcher.FetchPlainTextAsync("https://example.com/portal");

        Assert.True(result.Success);
        Assert.Contains("trámites", result.PlainText, StringComparison.OrdinalIgnoreCase);
        Assert.True(scraper.WasCalled);
    }

    [Theory]
    [InlineData("navigation_timeout", "La página tardó demasiado en cargar.")]
    [InlineData("pdf_not_supported", KbUrlClassifier.PdfNotSupportedMessage)]
    [InlineData("busy", "El servicio de renderizado está ocupado.")]
    public void TranslateScraperError_MapsKnownCodes(string code, string expected)
    {
        var message = KbScraperClient.TranslateScraperError(code, "fallback");
        Assert.Equal(expected, message);
    }

    private sealed class FakeKbScraperClient : IKbScraperClient
    {
        public string? Html { get; init; }
        public bool WasCalled { get; private set; }

        public Task<KbScraperRenderResult> RenderAsync(string url, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(new KbScraperRenderResult(
                true,
                Html ?? string.Empty,
                200,
                url,
                null));
        }
    }
}
