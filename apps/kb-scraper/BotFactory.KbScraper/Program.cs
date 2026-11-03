using BotFactory.KbScraper;
using BotFactory.KbScraper.Contracts;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ScraperOptions>(options =>
{
    options.ApiKey = builder.Configuration["API_KEY"] ?? builder.Configuration["Scraper:ApiKey"] ?? string.Empty;
    if (int.TryParse(builder.Configuration["MAX_CONCURRENT_RENDERS"], out var maxConcurrent))
        options.MaxConcurrentRenders = maxConcurrent;
    if (int.TryParse(builder.Configuration["DEFAULT_TIMEOUT_MS"], out var timeoutMs))
        options.DefaultTimeoutMs = timeoutMs;
    if (int.TryParse(builder.Configuration["POST_LOAD_WAIT_MS"], out var postLoadWaitMs))
        options.PostLoadWaitMs = postLoadWaitMs;
    if (int.TryParse(builder.Configuration["ACQUIRE_TIMEOUT_MS"], out var acquireTimeoutMs))
        options.AcquireTimeoutMs = acquireTimeoutMs;
});

builder.Services.AddSingleton<PlaywrightRenderService>();
builder.Services.AddSingleton<IRenderService>(sp => sp.GetRequiredService<PlaywrightRenderService>());

var app = builder.Build();
var options = app.Services.GetRequiredService<IOptions<ScraperOptions>>().Value;
if (string.IsNullOrWhiteSpace(options.ApiKey))
{
    app.Logger.LogWarning("API_KEY is not configured; /render will reject all requests.");
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/render", async (
    RenderRequest request,
    IRenderService renderer,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    if (!IsAuthorized(httpContext, options.ApiKey))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Url))
        return Results.Json(new RenderErrorResponse("invalid_url", "URL is required."), statusCode: 400);

    var (success, response, error) = await renderer.RenderAsync(request, ct);
    if (!success)
    {
        var status = error?.Error switch
        {
            "busy" => StatusCodes.Status503ServiceUnavailable,
            "navigation_timeout" or "navigation_failed" or "html_too_large" or "pdf_not_supported" =>
                StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(error, statusCode: status);
    }

    return Results.Json(response);
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    var renderer = app.Services.GetRequiredService<PlaywrightRenderService>();
    renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

app.Run();

static bool IsAuthorized(HttpContext context, string apiKey)
{
    if (string.IsNullOrWhiteSpace(apiKey))
        return false;

    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return false;

    var token = header["Bearer ".Length..].Trim();
    return string.Equals(token, apiKey, StringComparison.Ordinal);
}
