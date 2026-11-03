using System.Net.Http.Json;
using System.Text.Json;
using BotFactory.Core.Services;
using BotFactory.KbScraper.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Services;

public class KbScraperClient : IKbScraperClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan[] BusyRetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
    ];

    private readonly HttpClient _http;
    private readonly KbScraperOptions _options;
    private readonly ILogger<KbScraperClient> _logger;

    public KbScraperClient(HttpClient http, IOptions<KbScraperOptions> options, ILogger<KbScraperClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<KbScraperRenderResult> RenderAsync(string url, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new KbScraperRenderResult(
                false,
                null,
                0,
                null,
                "El servicio de renderizado no está configurado.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return new KbScraperRenderResult(
                false,
                null,
                0,
                null,
                "El servicio de renderizado no está configurado.");
        }

        KbScraperRenderResult? lastBusy = null;
        for (var attempt = 0; ; attempt++)
        {
            var result = await SendRenderOnceAsync(url, ct);
            if (result.Success || !result.Transient)
                return result;

            lastBusy = result;
            if (attempt >= BusyRetryDelays.Length)
                break;

            var delay = BusyRetryDelays[attempt];
            _logger.LogInformation(
                "KB scraper busy for {Url}; retrying in {DelaySeconds}s (attempt {Attempt}/{Max})",
                url,
                delay.TotalSeconds,
                attempt + 1,
                BusyRetryDelays.Length);
            await Task.Delay(delay, ct);
        }

        return lastBusy with
        {
            Error = "El servicio de renderizado está ocupado. Vuelva a reindexar en unos minutos.",
        };
    }

    private async Task<KbScraperRenderResult> SendRenderOnceAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "render");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");
        request.Content = JsonContent.Create(new RenderRequest(url, _options.TimeoutSeconds * 1000));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException)
        {
            return new KbScraperRenderResult(
                false,
                null,
                0,
                null,
                "La página tardó demasiado en cargar.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "KB scraper request failed for {Url}", url);
            return new KbScraperRenderResult(
                false,
                null,
                0,
                null,
                "El servicio de renderizado no está disponible. Intente más tarde.",
                Transient: true);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                return new KbScraperRenderResult(
                    false,
                    null,
                    503,
                    null,
                    "El servicio de renderizado está ocupado.",
                    Transient: true);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("KB scraper rejected API key");
                return new KbScraperRenderResult(
                    false,
                    null,
                    401,
                    null,
                    "El servicio de renderizado no está disponible. Intente más tarde.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryParseError(body);
                var message = TranslateScraperError(error?.Error, error?.Message);
                var transient = error?.Error is "busy";
                return new KbScraperRenderResult(
                    false,
                    null,
                    (int)response.StatusCode,
                    null,
                    message,
                    Transient: transient);
            }

            var render = JsonSerializer.Deserialize<RenderResponse>(body, JsonOptions);
            if (render is null || string.IsNullOrWhiteSpace(render.Html))
            {
                return new KbScraperRenderResult(
                    false,
                    null,
                    render?.StatusCode ?? 0,
                    render?.FinalUrl,
                    "La respuesta del servidor está vacía.");
            }

            return new KbScraperRenderResult(
                true,
                render.Html,
                render.StatusCode,
                render.FinalUrl,
                null);
        }
    }

    private static RenderErrorResponse? TryParseError(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<RenderErrorResponse>(body, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string TranslateScraperError(string? error, string? fallback)
    {
        return error switch
        {
            "navigation_timeout" => "La página tardó demasiado en cargar.",
            "navigation_failed" => "No se pudo cargar la página. Comprueba que la URL sea pública y accesible.",
            "html_too_large" => "La página supera el tamaño máximo permitido (5 MB). Usa una URL más liviana o sube un .txt.",
            "pdf_not_supported" => KbUrlClassifier.PdfNotSupportedMessage,
            "busy" => "El servicio de renderizado está ocupado.",
            _ => string.IsNullOrWhiteSpace(fallback)
                ? "No se pudo renderizar la página."
                : fallback,
        };
    }
}
