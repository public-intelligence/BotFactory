using System.Diagnostics;
using System.Text;
using BotFactory.KbScraper.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace BotFactory.KbScraper;

public sealed class PlaywrightRenderService : IRenderService, IAsyncDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly ScraperOptions _options;
    private readonly SemaphoreSlim _concurrency;
    private readonly ILogger<PlaywrightRenderService> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightRenderService(IOptions<ScraperOptions> options, ILogger<PlaywrightRenderService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRenders));
    }

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_browser is not null)
            return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task<(bool Success, RenderResponse? Response, RenderErrorResponse? Error)> RenderAsync(
        RenderRequest request,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, null, new RenderErrorResponse("invalid_url", "URL is not valid."));
        }

        // Queue behind in-flight renders instead of failing immediately (bulk reindex stampede).
        var acquireTimeout = TimeSpan.FromMilliseconds(Math.Max(0, _options.AcquireTimeoutMs));
        if (!await _concurrency.WaitAsync(acquireTimeout, ct))
        {
            return (false, null, new RenderErrorResponse("busy", "Scraper is at capacity."));
        }

        try
        {
            await EnsureInitializedAsync(ct);
            var timeoutMs = request.TimeoutMs ?? _options.DefaultTimeoutMs;
            var sw = Stopwatch.StartNew();

            await using var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = UserAgent,
                IgnoreHTTPSErrors = true,
            });

            var page = await context.NewPageAsync();
            IResponse? response;
            try
            {
                response = await page.GotoAsync(
                    request.Url,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.Load,
                        Timeout = timeoutMs,
                    });
            }
            catch (TimeoutException)
            {
                return (false, null, new RenderErrorResponse("navigation_timeout", "Page navigation timed out."));
            }
            catch (PlaywrightException ex)
            {
                _logger.LogWarning(ex, "Playwright navigation failed for {Url}", request.Url);
                return (false, null, new RenderErrorResponse("navigation_failed", ex.Message));
            }

            if (IsPdfResponse(response, page.Url))
            {
                return (false, null, new RenderErrorResponse("pdf_not_supported", "URL returned PDF content."));
            }

            if (_options.PostLoadWaitMs > 0)
                await page.WaitForTimeoutAsync(_options.PostLoadWaitMs);

            var html = await page.ContentAsync();
            if (Encoding.UTF8.GetByteCount(html) > _options.MaxHtmlBytes)
            {
                return (false, null, new RenderErrorResponse("html_too_large", "Rendered HTML exceeds size limit."));
            }

            var statusCode = response?.Status ?? 0;
            sw.Stop();
            return (true, new RenderResponse(html, page.Url, statusCode, sw.ElapsedMilliseconds), null);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private static bool IsPdfResponse(IResponse? response, string finalUrl)
    {
        if (finalUrl.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
            return true;

        var contentType = response?.Headers
            .FirstOrDefault(h => h.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            .Value;
        return contentType?.Contains("application/pdf", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _concurrency.Dispose();
    }
}
