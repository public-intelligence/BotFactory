using System.Text.Json;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Services;

namespace BotFactory.Infrastructure.Tools;

public class KbSearchToolHandler : IToolHandler
{
    public const string ToolName = "kb_search";

    private static readonly FunctionDeclaration DeclarationValue = new()
    {
        Name = ToolName,
        Description =
            "Busca en la base de conocimiento del bot información relevante para la pregunta del usuario. "
            + "Úsala cuando el usuario pregunte detalles que el prompt principal no puede responder por sí solo.",
        Parameters = new JsonSchema
        {
            Type = "object",
            Properties = new()
            {
                ["query"] = new JsonSchema
                {
                    Type = "string",
                    Description =
                        "Consulta de búsqueda en lenguaje natural. Formula siempre la consulta en español.",
                },
            },
            Required = new[] { "query" },
        },
    };

    private readonly IKbSearchService _kbSearch;

    public KbSearchToolHandler(IKbSearchService kbSearch)
    {
        _kbSearch = kbSearch;
    }

    public string Name => ToolName;

    public FunctionDeclaration Declaration => DeclarationValue;

    public async Task<ToolInvocation> ExecuteAsync(
        JsonElement args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var query = args.TryGetProperty("query", out var qEl)
            ? qEl.GetString() ?? string.Empty
            : string.Empty;

        var results = await _kbSearch.SearchAsync(
            context.Bot.Id,
            query,
            context.Bot.TopK,
            context.Bot.SimilarityThreshold,
            cancellationToken);

        // Wire payload returned to the model — chunk text only. The model
        // doesn't need filenames or distances to answer; those are UI metadata.
        var responsePayload = new
        {
            results = results.Select(r => new
            {
                chunkId = r.ChunkId,
                text = r.Text,
            }).ToList(),
        };

        // Debug-only telemetry payload (admins inspect the raw event row).
        var telemetryRows = results.Select(r => new
        {
            chunkId = r.ChunkId,
            documentId = r.DocumentId,
            filename = r.DocumentFilename,
            sourceLocation = r.SourceLocation,
            distance = r.Distance,
        }).ToList();

        var display = results.Select(r => new ToolDisplayItem(
            Id: r.ChunkId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Label: BuildLabel(r.DocumentFilename, r.SourceLocation),
            Body: r.Text)).ToList();

        var noUsable = results.Count == 0;
        var summary = $"query=\"{query}\" · {results.Count} result(s)";

        return new ToolInvocation(
            ResponsePayload: responsePayload,
            Telemetry: new ToolTelemetry(
                EventName: "kb_retrieved",
                Stage: "retrieval",
                Data: new { query, results = telemetryRows }),
            NoUsableResults: noUsable,
            Display: display,
            Summary: summary);
    }

    private static string BuildLabel(string filename, string sourceLocation)
    {
        var fname = string.IsNullOrWhiteSpace(filename) ? "doc" : filename;
        var range = ParseCharRange(sourceLocation);
        return range is { } r
            ? $"{fname}:{r.Start}-{r.End}"
            : $"{fname}";
    }

    private static (int Start, int End)? ParseCharRange(string? sourceLocation)
    {
        if (string.IsNullOrEmpty(sourceLocation)
            || !sourceLocation.StartsWith("char:", StringComparison.Ordinal))
        {
            return null;
        }

        var range = sourceLocation.AsSpan(5);
        var dash = range.IndexOf('-');
        if (dash < 0) return null;
        if (!int.TryParse(range[..dash], out var s)) return null;
        if (!int.TryParse(range[(dash + 1)..], out var e)) return null;
        return (s, e);
    }
}
