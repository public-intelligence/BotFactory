using System.Text;
using System.Text.Json;
using BotFactory.Api.Auth;
using BotFactory.Api.Logging;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;

namespace BotFactory.Api.Endpoints;

public static class SystemLogEndpoints
{
    // How much of the file tail is scanned per request. Filters apply within
    // this window only — plenty for a live viewer, and keeps a 25MB file from
    // being re-read on every poll.
    private const int ReadBudgetBytes = 4 * 1024 * 1024;

    private static readonly Dictionary<string, int> LevelRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Verbose"] = 0,
        ["Debug"] = 1,
        ["Information"] = 2,
        ["Warning"] = 3,
        ["Error"] = 4,
        ["Fatal"] = 5,
    };

    public static void MapSystemLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/logs")
            .RequireAuthorization()
            .WithTags("SystemLogs");

        // SuperAdmin here means "admins only": the permission filter also lets
        // bot:admin through, matching the Hangfire dashboard gate.
        group.MapGet("/", (
            IConfiguration config,
            int take = 200,
            string? level = null,
            string? search = null) =>
        {
            take = Math.Clamp(take, 1, 1000);
            var file = FileLog.CurrentFile(config);
            if (file is null)
            {
                return Results.Ok(new SystemLogTailResponse([], 0, ""));
            }

            var entries = ReadTail(file, take, level, search);
            return Results.Ok(new SystemLogTailResponse(entries, file.Length, file.Name));
        })
        .RequirePermission(Permissions.SuperAdmin)
        .WithName("GetSystemLogs")
        .Produces<SystemLogTailResponse>();

        group.MapGet("/raw", (IConfiguration config, bool download = false) =>
        {
            var file = FileLog.CurrentFile(config);
            if (file is null)
            {
                return Results.NotFound();
            }

            var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Results.File(stream, "text/plain; charset=utf-8",
                download ? file.Name : null);
        })
        .RequirePermission(Permissions.SuperAdmin)
        .WithName("GetSystemLogsRaw")
        .ExcludeFromDescription();

        // Support drop-box relay. Customer deployments have locked-down egress,
        // so their diagnose script posts bundles to OUR deployment (publicly
        // /v0/api/logs — the SWA and nginx rewrite it here) and this endpoint
        // forwards to the real drop box. Anonymous on purpose: the caller's
        // bearer key passes through and the drop box validates it; this relay
        // holds no secrets and can only ever talk to the one configured URL.
        group.MapPost("/upload", async (
            HttpContext ctx,
            IHttpClientFactory httpClientFactory,
            IConfiguration config) =>
        {
            var target = config["SupportDropBox:Url"] ?? "https://willy.im/api/logs";
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(auth))
            {
                return Results.Unauthorized();
            }
            if (ctx.Request.ContentLength is > MaxUploadBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var client = httpClientFactory.CreateClient("support-dropbox");
            using var upstream = new HttpRequestMessage(HttpMethod.Post, target + ctx.Request.QueryString);
            upstream.Headers.TryAddWithoutValidation("Authorization", auth);
            upstream.Content = new StreamContent(ctx.Request.Body);
            if (ctx.Request.ContentLength is { } len)
            {
                upstream.Content.Headers.ContentLength = len;
            }
            if (!string.IsNullOrEmpty(ctx.Request.ContentType))
            {
                upstream.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
            }

            using var response = await client.SendAsync(upstream, ctx.RequestAborted);
            var body = await response.Content.ReadAsStringAsync(ctx.RequestAborted);
            return Results.Text(body,
                response.Content.Headers.ContentType?.ToString() ?? "application/json",
                statusCode: (int)response.StatusCode);
        })
        .AllowAnonymous()
        .WithName("UploadSupportLogs")
        .ExcludeFromDescription();
    }

    // The diagnose script caps bundles at 8 MB; anything bigger is not ours.
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private static List<SystemLogEntryResponse> ReadTail(FileInfo file, int take, string? level, string? search)
    {
        // The sink holds the file open; read whatever is there right now.
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var skippedPartialFirstLine = false;
        if (stream.Length > ReadBudgetBytes)
        {
            stream.Seek(-ReadBudgetBytes, SeekOrigin.End);
            skippedPartialFirstLine = true;
        }

        var minRank = level is not null && LevelRank.TryGetValue(level, out var r) ? r : 0;

        var entries = new List<SystemLogEntryResponse>();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (skippedPartialFirstLine)
            {
                skippedPartialFirstLine = false;
                continue;
            }
            var entry = ParseLine(line);
            if (entry is null) continue;
            if (LevelRank.GetValueOrDefault(entry.Level, 2) < minRank) continue;
            if (!string.IsNullOrWhiteSpace(search) && !Matches(entry, search)) continue;
            entries.Add(entry);
        }

        // Chronological order, trimmed to the most recent `take`.
        return entries.Count > take ? entries[^take..] : entries;
    }

    private static bool Matches(SystemLogEntryResponse entry, string search) =>
        entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        (entry.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (entry.Category?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    private static SystemLogEntryResponse? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var timestamp = root.TryGetProperty("@t", out var t)
                ? t.GetDateTime()
                : default;
            // CLEF omits @l for Information-level events.
            var lvl = root.TryGetProperty("@l", out var l) ? l.GetString() ?? "Information" : "Information";
            var message = root.TryGetProperty("@m", out var m) ? m.GetString() ?? "" : "";
            var exception = root.TryGetProperty("@x", out var x) ? x.GetString() : null;
            var category = root.TryGetProperty("SourceContext", out var sc) ? sc.GetString() : null;
            return new SystemLogEntryResponse(timestamp, lvl, message, exception, category);
        }
        catch (JsonException)
        {
            // A line torn by the size-limit roll or a concurrent write; skip it.
            return null;
        }
    }
}
