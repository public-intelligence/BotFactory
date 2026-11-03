using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Api.Endpoints;

public static class DebugEndpoints
{
    public static void MapDebugEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/debug")
            .RequireAuthorization()
            .WithTags("Debug");

        group.MapPost("/search", async (
            SearchRequest request,
            KbSearchService kbSearch,
            BotFactoryDbContext db,
            CancellationToken ct) =>
        {
            double threshold;
            if (request.MaxDistance.HasValue)
            {
                threshold = request.MaxDistance.Value;
            }
            else
            {
                var bot = await db.Bots.FindAsync([request.BotId], ct);
                threshold = bot?.SimilarityThreshold ?? 0.6;
            }

            var kb = await kbSearch.SearchAsync(request.BotId, request.Query, request.TopK, threshold, ct);
            var results = kb.Select(r => new SearchResultItem(
                r.ChunkId, r.DocumentId, r.ChunkIndex, r.Text, r.SourceLocation, r.Distance)).ToList();

            return Results.Ok(new SearchResponse(request.Query, results));
        })
        .WithName("DebugSearch")
        .Produces<SearchResponse>();
    }

    public record SearchRequest(string Query, int BotId, int TopK = 5, double? MaxDistance = null);
    public record SearchResultItem(int ChunkId, int DocumentId, int ChunkIndex, string Text, string SourceLocation, double Distance);
    public record SearchResponse(string Query, List<SearchResultItem> Results);
}
