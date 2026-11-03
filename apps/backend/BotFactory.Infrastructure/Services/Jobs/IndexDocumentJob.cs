using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;

using Microsoft.SemanticKernel.Text;

namespace BotFactory.Infrastructure.Services.Jobs;

public class IndexDocumentJob
{
    private readonly ITenantContextSetter _tenantSetter;
    private readonly BotFactoryDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly GeminiEmbeddingService _embeddings;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<IndexDocumentJob> _logger;

    // Chunking parameters — infra-level tuning knobs, not exposed via API.
    // Could be moved to per-bot config if needed, but hardcoded is fine for now.
    private const int MaxTokensPerLine = 200;
    private const int MaxTokensPerParagraph = 800;
    private const int OverlapTokens = 100;

    public IndexDocumentJob(
        ITenantContextSetter tenantSetter,
        BotFactoryDbContext db,
        IBlobStorageService blobStorage,
        GeminiEmbeddingService embeddings,
        ITelemetryService telemetry,
        ILogger<IndexDocumentJob> logger)
    {
        _tenantSetter = tenantSetter;
        _db = db;
        _blobStorage = blobStorage;
        _embeddings = embeddings;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task ExecuteAsync(int documentId, string organizationId)
    {
        _tenantSetter.SetTenant(organizationId);

        var doc = await _db.KbDocuments.FindAsync(documentId);
        if (doc is null)
        {
            _logger.LogWarning("Document {DocumentId} not found, skipping", documentId);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _telemetry.EmitAsync(new TelemetryEmit(
            "kb_index_started",
            EventSource.Backend,
            BotId: doc.BotId,
            Stage: "kb_index"));

        try
        {

            // 1. Download content from S3
            using var stream = await _blobStorage.DownloadAsync(doc.BlobKey);
            using var reader = new StreamReader(stream);
            var fullText = await reader.ReadToEndAsync();

            // 2. Chunk the text
            // Gemini has no .NET tokenizer library. GPT-4o's tiktoken (o200k_base) is a
            // convenient approximation — token counts are close enough across modern tokenizers
            // for chunking purposes, and avoids calling Gemini's countTokens API per chunk.
            var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            int CountTokens(string s) => tokenizer.CountTokens(s);

            var lines = TextChunker.SplitPlainTextLines(fullText, MaxTokensPerLine, CountTokens);
            var chunks = TextChunker.SplitPlainTextParagraphs(lines, MaxTokensPerParagraph, OverlapTokens, tokenCounter: CountTokens);

            _logger.LogInformation("Document {DocumentId}: {ChunkCount} chunks from {TextLength} chars",
                documentId, chunks.Count, fullText.Length);

            if (chunks.Count == 0)
            {
                _logger.LogWarning("Document {DocumentId} produced 0 chunks", documentId);
                doc.Status = DocumentStatus.Failed;
                doc.ErrorMessage = "El documento no produjo texto usable para indexar. Revisa que el archivo o la URL tenga contenido suficiente.";
                await _db.SaveChangesAsync();
                return;
            }

            // 3. Generate embeddings
            var embeddings = await _embeddings.GenerateEmbeddingsAsync(chunks);

            // 4. Delete old chunks, insert new ones, and mark as indexed atomically
            await using var tx = await _db.Database.BeginTransactionAsync();

            var oldChunks = await _db.KbChunks
                .Where(c => c.KbDocumentId == documentId)
                .ToListAsync();
            _db.KbChunks.RemoveRange(oldChunks);

            // 5. Compute source locations and store new chunks
            var overlapChars = EstimateOverlapChars(chunks);
            var charStart = 0;

            for (int i = 0; i < chunks.Count; i++)
            {
                var charEnd = charStart + chunks[i].Length;
                var chunk = new KbChunk
                {
                    KbDocumentId = documentId,
                    ChunkIndex = i,
                    Text = chunks[i],
                    SourceLocation = $"char:{charStart}-{charEnd}",
                };

                chunk.Embedding = new SqlVector<float>(embeddings[i]);
                _db.KbChunks.Add(chunk);

                charStart = charEnd - overlapChars;
            }

            // 6. Update document status
            doc.Status = DocumentStatus.Indexed;
            doc.IndexedAt = DateTime.UtcNow;
            doc.ErrorMessage = null;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            sw.Stop();
            await _telemetry.EmitAsync(new TelemetryEmit(
                "kb_index_completed",
                EventSource.Backend,
                BotId: doc.BotId,
                Stage: "kb_index",
                LatencyMs: (int)sw.ElapsedMilliseconds));

            _logger.LogInformation("Document {DocumentId} indexed successfully with {ChunkCount} chunks",
                documentId, chunks.Count);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _telemetry.EmitAsync(new TelemetryEmit(
                "kb_index_failed",
                EventSource.Backend,
                BotId: doc.BotId,
                Stage: "kb_index",
                LatencyMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: ex.Message));

            _logger.LogError(ex, "Failed to index document {DocumentId}", documentId);

            // Keep serving previous chunks on refresh failure.
            var hasChunks = await _db.KbChunks.AnyAsync(c => c.KbDocumentId == documentId);
            if (hasChunks)
            {
                doc.Status = DocumentStatus.Indexed;
                doc.IndexedAt ??= DateTime.UtcNow;
            }
            else
            {
                doc.Status = DocumentStatus.Failed;
                doc.ErrorMessage = Truncate($"Error al indexar: {ex.Message}");
            }

            await _db.SaveChangesAsync();
            throw; // Re-throw so Hangfire retries
        }
    }

    private static string Truncate(string message, int max = 1000)
    {
        message = message.Trim();
        return message.Length <= max ? message : message[..(max - 1)] + "…";
    }

    private static int EstimateOverlapChars(List<string> chunks)
    {
        if (chunks.Count < 2) return 0;

        // Find the actual overlap by comparing consecutive chunks
        var first = chunks[0];
        var second = chunks[1];

        // Check how much of the end of chunk 0 appears at the start of chunk 1
        var maxCheck = Math.Min(first.Length, second.Length) / 2;
        for (int len = maxCheck; len > 0; len--)
        {
            var tail = first[^len..];
            if (second.StartsWith(tail, StringComparison.Ordinal))
                return len;
        }

        return 0;
    }
}
