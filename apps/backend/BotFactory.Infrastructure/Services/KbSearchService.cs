using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public record KbSearchResult(
    int ChunkId,
    int DocumentId,
    int ChunkIndex,
    string Text,
    string SourceLocation,
    string DocumentFilename,
    double Distance);

public interface IKbSearchService
{
    Task<List<KbSearchResult>> SearchAsync(
        int botId,
        string query,
        int topK,
        double maxDistance,
        CancellationToken ct = default);
}

public class KbSearchService : IKbSearchService
{
    private readonly BotFactoryDbContext _db;
    private readonly GeminiEmbeddingService _embedding;
    private readonly ITenantContext _tenant;

    public KbSearchService(
        BotFactoryDbContext db,
        GeminiEmbeddingService embedding,
        ITenantContext tenant)
    {
        _db = db;
        _embedding = embedding;
        _tenant = tenant;
    }

    public async Task<List<KbSearchResult>> SearchAsync(
        int botId,
        string query,
        int topK,
        double maxDistance,
        CancellationToken ct = default)
    {
        var queryEmbedding = await _embedding.GenerateQueryEmbeddingAsync(query, ct);
        var queryVector = new SqlVector<float>(queryEmbedding);
        var orgId = _tenant.OrganizationId;

        return await _db.Database.SqlQuery<KbSearchResult>(
            $"""
            ;WITH ranked AS (
                SELECT c.Id, c.KbDocumentId, c.ChunkIndex, c.Text, c.SourceLocation, d.Filename,
                       VECTOR_DISTANCE('cosine', c.Embedding, {queryVector}) AS Distance
                FROM kb_chunks c
                INNER JOIN kb_documents d ON c.KbDocumentId = d.Id
                WHERE c.OrganizationId = {orgId}
                  AND d.BotId = {botId}
            )
            SELECT Id AS ChunkId, KbDocumentId AS DocumentId, ChunkIndex, Text, SourceLocation,
                   Filename AS DocumentFilename, Distance
            FROM ranked
            WHERE Distance < {maxDistance}
            ORDER BY Distance
            OFFSET 0 ROWS FETCH NEXT {topK} ROWS ONLY
            """
        ).ToListAsync(ct);
    }

}
