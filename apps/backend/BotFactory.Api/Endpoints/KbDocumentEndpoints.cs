using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Services;
using BotFactory.Infrastructure.Services.Jobs;

namespace BotFactory.Api.Endpoints;

public static class KbDocumentEndpoints
{
    public static void MapKbDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bots/{botId:int}/documents")
            .RequireAuthorization()
            .WithTags("KbDocuments");

        group.MapGet("/", async (
            int botId,
            IBotService botService,
            IKbDocumentService docService,
            int page = 1,
            int pageSize = 10,
            string? sortBy = null,
            string? sortDir = null,
            string? search = null,
            string? status = null,
            string? sourceType = null) =>
        {
            var bot = await botService.GetByIdAsync(botId);
            if (bot is null) return Results.NotFound();

            var (docs, totalCount) = await docService.GetByBotIdAsync(
                botId, page, pageSize, sortBy, sortDir, search, status, sourceType);
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            return Results.Ok(new PaginatedResponse<KbDocumentResponse>(
                docs.Select(ToResponse).ToList(),
                page, pageSize, totalCount, totalPages));
        })
        .RequirePermission(Permissions.BotRead)
        .WithName("GetKbDocuments")
        .Produces<PaginatedResponse<KbDocumentResponse>>();

        group.MapPost("/bulk-reindex", async (
            int botId,
            BulkKbDocumentsRequest request,
            IBotService botService,
            IKbDocumentService docService,
            KbIndexingOrchestrator orchestrator,
            ITenantContext tenantContext,
            BotFactory.Infrastructure.Data.BotFactoryDbContext db,
            ILogger<KbDocument> logger) =>
        {
            var bot = await botService.GetByIdAsync(botId);
            if (bot is null) return Results.NotFound();

            List<KbDocument> docs;
            try
            {
                docs = await docService.ResolveBulkAsync(
                    botId, request.DocumentIds, request.Status, request.SourceType, request.Search);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            foreach (var doc in docs)
            {
                doc.Status = DocumentStatus.Pending;
                doc.ErrorMessage = null;
            }

            if (docs.Count > 0)
                await db.SaveChangesAsync();

            foreach (var doc in docs)
                await orchestrator.TryEnqueueForDocumentAsync(doc, tenantContext.OrganizationId);

            logger.LogInformation("KB bulk reindex: {Count} documents for bot {BotId}", docs.Count, botId);
            return Results.Accepted(value: new BulkKbDocumentsResponse(docs.Count));
        })
        .RequirePermission(Permissions.KbUpdate)
        .WithName("BulkReindexKbDocuments")
        .Produces<BulkKbDocumentsResponse>(202)
        .Produces(400)
        .Produces(404);

        group.MapPost("/bulk-delete", async (
            int botId,
            BulkKbDocumentsRequest request,
            IBotService botService,
            IKbDocumentService docService,
            ILogger<KbDocument> logger) =>
        {
            var bot = await botService.GetByIdAsync(botId);
            if (bot is null) return Results.NotFound();

            List<KbDocument> docs;
            try
            {
                docs = await docService.ResolveBulkAsync(
                    botId, request.DocumentIds, request.Status, request.SourceType, request.Search);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var count = await docService.DeleteManyAsync(botId, docs);
            logger.LogInformation("KB bulk delete: {Count} documents for bot {BotId}", count, botId);
            return Results.Ok(new BulkKbDocumentsResponse(count));
        })
        .RequirePermission(Permissions.KbDelete)
        .WithName("BulkDeleteKbDocuments")
        .Produces<BulkKbDocumentsResponse>()
        .Produces(400)
        .Produces(404);

        group.MapPost("/", async (
            int botId,
            IFormFile file,
            IBotService botService,
            IKbDocumentService docService,
            KbIndexingOrchestrator orchestrator,
            ITenantContext tenantContext,
            ILogger<KbDocument> logger) =>
        {
            var bot = await botService.GetByIdAsync(botId);
            if (bot is null) return Results.NotFound();

            if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only .txt files are allowed." });

            if (file.Length > 5 * 1024 * 1024)
                return Results.BadRequest(new { error = "File size must be under 5MB." });

            using var stream = file.OpenReadStream();
            var doc = await docService.CreateAsync(botId, file.FileName, stream);

            await orchestrator.TryEnqueueIndexAsync(doc.Id, tenantContext.OrganizationId);

            logger.LogInformation("KB document uploaded: {DocId} \"{Filename}\" for bot {BotId}", doc.Id, doc.Filename, botId);
            return Results.Created($"/api/bots/{botId}/documents/{doc.Id}", ToResponse(doc));
        })
        .RequirePermission(Permissions.KbUpdate)
        .WithName("UploadKbDocument")
        .Produces<KbDocumentResponse>(201)
        .DisableAntiforgery();

        group.MapPost("/from-urls", async (
            int botId,
            CreateKbDocumentsFromUrlsRequest request,
            IBotService botService,
            IKbDocumentService docService,
            KbIndexingOrchestrator orchestrator,
            ITenantContext tenantContext,
            ILogger<KbDocument> logger) =>
        {
            var bot = await botService.GetByIdAsync(botId);
            if (bot is null) return Results.NotFound();

            if (request.Urls is null || request.Urls.Count == 0)
                return Results.BadRequest(new { error = "At least one URL is required." });

            if (request.Urls.Count > IKbDocumentService.MaxUrlsPerRequest)
                return Results.BadRequest(new { error = $"A maximum of {IKbDocumentService.MaxUrlsPerRequest} URLs is allowed per request." });

            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in request.Urls)
            {
                if (!KbUrlValidator.TryNormalize(raw, out var url, out var error))
                    return Results.BadRequest(new { error });

                if (seen.Add(url))
                    normalized.Add(url);
            }

            if (normalized.Count == 0)
                return Results.BadRequest(new { error = "At least one valid URL is required." });

            // SSRF resolve check for each URL before creating rows.
            // PDF URLs are skipped (not a hard fail) so a spreadsheet with one PDF
            // does not reject the whole batch.
            var skippedPdfCount = 0;
            var htmlUrls = new List<string>();
            foreach (var url in normalized)
            {
                var uri = new Uri(url);
                if (KbUrlClassifier.IsPdfUrl(uri))
                {
                    skippedPdfCount++;
                    continue;
                }

                var (ok, error) = await KbUrlValidator.IsSafeToFetchAsync(uri);
                if (!ok)
                    return Results.BadRequest(new { error });

                htmlUrls.Add(url);
            }

            if (htmlUrls.Count == 0)
            {
                return Results.BadRequest(new
                {
                    error = skippedPdfCount > 0
                        ? KbUrlClassifier.PdfNotSupportedMessage
                        : "At least one valid URL is required."
                });
            }

            var (docs, skippedExisting) = await docService.CreateFromUrlsAsync(botId, htmlUrls);
            foreach (var doc in docs)
                await orchestrator.TryEnqueueIngestAsync(doc.Id, tenantContext.OrganizationId);

            logger.LogInformation(
                "KB URL documents created: {Created} (skipped existing: {Skipped}, skipped PDF: {SkippedPdf}) for bot {BotId}",
                docs.Count,
                skippedExisting,
                skippedPdfCount,
                botId);

            var response = new CreateKbDocumentsFromUrlsResponse(
                docs.Select(ToResponse).ToList(),
                skippedExisting,
                skippedPdfCount);

            return docs.Count == 0
                ? Results.Ok(response)
                : Results.Created($"/api/bots/{botId}/documents", response);
        })
        .RequirePermission(Permissions.KbUpdate)
        .WithName("CreateKbDocumentsFromUrls")
        .Produces<CreateKbDocumentsFromUrlsResponse>(201)
        .Produces<CreateKbDocumentsFromUrlsResponse>(200)
        .Produces(400)
        .Produces(404);

        group.MapGet("/{docId:int}", async (int botId, int docId, IKbDocumentService docService) =>
        {
            var doc = await docService.GetByIdAsync(botId, docId);
            return doc is null ? Results.NotFound() : Results.Ok(ToResponse(doc));
        })
        .RequirePermission(Permissions.BotRead)
        .WithName("GetKbDocument")
        .Produces<KbDocumentResponse>()
        .Produces(404);

        group.MapGet("/{docId:int}/download", async (int botId, int docId, IKbDocumentService docService, HttpContext http, CancellationToken ct) =>
        {
            var opened = await docService.OpenReadAsync(botId, docId, ct);
            return await BlobDownloadResult.StreamAsync(
                http,
                opened?.Blob,
                BlobDownloadResult.AdminCacheControl,
                fileDownloadName: opened?.Filename);
        })
        .RequirePermission(Permissions.BotRead)
        .WithName("GetKbDocumentDownloadUrl")
        .Produces<byte[]>(200, "application/octet-stream")
        .Produces(404);

        group.MapGet("/{docId:int}/content", async (int botId, int docId, IKbDocumentService docService, CancellationToken ct) =>
        {
            var result = await docService.GetContentAsync(botId, docId, ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(new KbDocumentContentResponse(result.Value.Filename, result.Value.Content));
        })
        .RequirePermission(Permissions.BotRead)
        .WithName("GetKbDocumentContent")
        .Produces<KbDocumentContentResponse>()
        .Produces(404);

        group.MapDelete("/{docId:int}", async (int botId, int docId, IKbDocumentService docService, ILogger<KbDocument> logger) =>
        {
            var deleted = await docService.DeleteAsync(botId, docId);
            if (deleted)
                logger.LogInformation("KB document {DocId} deleted from bot {BotId}", docId, botId);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequirePermission(Permissions.KbDelete)
        .WithName("DeleteKbDocument")
        .Produces(204)
        .Produces(404);

        group.MapPost("/{docId:int}/reindex", async (
            int botId, int docId,
            IKbDocumentService docService,
            KbIndexingOrchestrator orchestrator,
            ITenantContext tenantContext,
            BotFactory.Infrastructure.Data.BotFactoryDbContext db) =>
        {
            var doc = await docService.GetByIdAsync(botId, docId);
            if (doc is null) return Results.NotFound();

            // Keep IndexedAt until a full success replaces it (so refresh failures can restore Indexed).
            doc.Status = DocumentStatus.Pending;
            doc.ErrorMessage = null;
            await db.SaveChangesAsync();

            await orchestrator.TryEnqueueForDocumentAsync(doc, tenantContext.OrganizationId);

            return Results.Accepted(value: ToResponse(doc));
        })
        .RequirePermission(Permissions.KbUpdate)
        .WithName("ReindexKbDocument")
        .Produces<KbDocumentResponse>(202)
        .Produces(404);
    }

    private static KbDocumentResponse ToResponse(KbDocument doc) => new(
        doc.Id,
        doc.BotId,
        doc.Filename,
        doc.Status.ToString(),
        doc.SourceType.ToString(),
        doc.SourceUrl,
        doc.IndexedAt,
        doc.CreatedAt,
        doc.UpdatedAt,
        doc.ErrorMessage);
}
