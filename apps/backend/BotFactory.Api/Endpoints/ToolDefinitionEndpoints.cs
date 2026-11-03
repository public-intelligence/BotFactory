using System.Text.Json;
using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Tools;
using BotFactory.Infrastructure.Tools.Execution;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Api.Endpoints;

public static class ToolDefinitionEndpoints
{
    private static readonly HashSet<string> AllowedHttpMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "PATCH", "DELETE" };

    public static void MapToolDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tools")
            .RequireAuthorization()
            .WithTags("Tools");

        group.MapGet("/", async (
            BotFactoryDbContext db,
            string? search,
            string? status,
            int? connectionId,
            int page = 1,
            int pageSize = 25) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.ToolDefinitions
                .AsNoTracking()
                .Include(d => d.Connection)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(d => d.Name.Contains(search) || d.DisplayName.Contains(search));

            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<ToolDefinitionStatus>(status, true, out var statusFilter))
            {
                query = query.Where(d => d.Status == statusFilter);
            }

            if (connectionId is not null)
                query = query.Where(d => d.ToolConnectionId == connectionId);

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(d => d.DisplayName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new
                {
                    Definition = d,
                    BotCount = db.BotTools.Count(bt => bt.ToolDefinitionId == d.Id && bt.Enabled),
                })
                .ToListAsync();

            return Results.Ok(new PaginatedResponse<ToolDefinitionResponse>(
                items.Select(x => ToResponse(x.Definition, x.BotCount)).ToList(),
                page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
        })
        .RequirePermission(Permissions.ToolRead)
        .WithName("GetToolDefinitions")
        .Produces<PaginatedResponse<ToolDefinitionResponse>>();

        group.MapGet("/{id:int}", async (int id, BotFactoryDbContext db) =>
        {
            var definition = await db.ToolDefinitions
                .AsNoTracking()
                .Include(d => d.Connection)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (definition is null)
                return Results.NotFound();

            var botCount = await db.BotTools.CountAsync(bt => bt.ToolDefinitionId == id && bt.Enabled);
            return Results.Ok(ToResponse(definition, botCount));
        })
        .RequirePermission(Permissions.ToolRead)
        .WithName("GetToolDefinition")
        .Produces<ToolDefinitionResponse>()
        .Produces(404);

        group.MapPost("/", async (
            CreateToolDefinitionRequest request,
            BotFactoryDbContext db,
            IEnumerable<IToolHandler> builtInHandlers) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;
            var error = ToolNameRules.Validate(name, builtInHandlers)
                ?? ValidateDefinitionFields(
                    request.DisplayName, request.AiDescription, request.HttpMethod,
                    request.PathTemplate, request.ParametersSchemaJson,
                    request.QueryParamsJson, request.StaticHeadersJson,
                    request.BodyTemplateJson, request.ErrorMappingJson,
                    request.DataClassification, out var dataClassification);

            if (error is not null)
                return Results.BadRequest(new { error });

            Enum.TryParse(request.DataClassification ?? nameof(ToolDataClassification.Internal),
                true, out ToolDataClassification classification);

            var connection = await db.ToolConnections.FirstOrDefaultAsync(c => c.Id == request.ToolConnectionId);
            if (connection is null)
                return Results.BadRequest(new { error = "La conexión indicada no existe." });

            if (await db.ToolDefinitions.AnyAsync(d => d.Name == name))
                return Results.Conflict(new { error = $"Ya existe una herramienta llamada '{name}'." });

            var definition = new ToolDefinition
            {
                ToolConnectionId = connection.Id,
                Name = name,
                DisplayName = request.DisplayName.Trim(),
                Category = request.Category,
                SupportContact = request.SupportContact,
                AiDescription = request.AiDescription.Trim(),
                AiUsageNotes = request.AiUsageNotes,
                HttpMethod = request.HttpMethod.ToUpperInvariant(),
                PathTemplate = NormalizePath(request.PathTemplate),
                QueryParamsJson = NullIfEmpty(request.QueryParamsJson),
                StaticHeadersJson = NullIfEmpty(request.StaticHeadersJson),
                BodyTemplateJson = NullIfEmpty(request.BodyTemplateJson),
                ParametersSchemaJson = request.ParametersSchemaJson,
                ResponseMappingJson = NullIfEmpty(request.ResponseMappingJson),
                ErrorMappingJson = NullIfEmpty(request.ErrorMappingJson),
                NormalizeArgDiacritics = request.NormalizeArgDiacritics ?? false,
                RequiresAuthenticatedUser = request.RequiresAuthenticatedUser ?? true,
                MaxCallsPerConversation = request.MaxCallsPerConversation,
                TimeoutMsOverride = request.TimeoutMsOverride,
                DataClassification = classification,
            };

            db.ToolDefinitions.Add(definition);
            await db.SaveChangesAsync();

            definition.Connection = connection;
            return Results.Created($"/api/tools/{definition.Id}", ToResponse(definition, 0));
        })
        .RequirePermission(Permissions.ToolUpdate)
        .WithName("CreateToolDefinition")
        .Produces<ToolDefinitionResponse>(201)
        .Produces(400)
        .Produces(409);

        group.MapPut("/{id:int}", async (
            int id,
            UpdateToolDefinitionRequest request,
            BotFactoryDbContext db,
            IEnumerable<IToolHandler> builtInHandlers) =>
        {
            var definition = await db.ToolDefinitions
                .Include(d => d.Connection)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (definition is null)
                return Results.NotFound();

            if (request.Name is not null && request.Name.Trim() != definition.Name)
            {
                var name = request.Name.Trim();
                var nameError = ToolNameRules.Validate(name, builtInHandlers);
                if (nameError is not null)
                    return Results.BadRequest(new { error = nameError });
                if (await db.ToolDefinitions.AnyAsync(d => d.Name == name && d.Id != id))
                    return Results.Conflict(new { error = $"Ya existe una herramienta llamada '{name}'." });
                definition.Name = name;
            }

            if (request.ToolConnectionId is not null && request.ToolConnectionId != definition.ToolConnectionId)
            {
                var connection = await db.ToolConnections.FirstOrDefaultAsync(c => c.Id == request.ToolConnectionId);
                if (connection is null)
                    return Results.BadRequest(new { error = "La conexión indicada no existe." });
                definition.ToolConnectionId = connection.Id;
                definition.Connection = connection;
            }

            if (request.HttpMethod is not null)
            {
                if (!AllowedHttpMethods.Contains(request.HttpMethod))
                    return Results.BadRequest(new { error = $"Método HTTP inválido: {request.HttpMethod}" });
                definition.HttpMethod = request.HttpMethod.ToUpperInvariant();
            }

            if (request.PathTemplate is not null)
            {
                if (string.IsNullOrWhiteSpace(request.PathTemplate))
                    return Results.BadRequest(new { error = "La plantilla de ruta es obligatoria." });
                definition.PathTemplate = NormalizePath(request.PathTemplate);
            }

            if (request.ParametersSchemaJson is not null)
            {
                var schemaError = ToolArgumentValidator.ValidateSchemaDocument(request.ParametersSchemaJson);
                if (schemaError is not null)
                    return Results.BadRequest(new { error = schemaError });
                definition.ParametersSchemaJson = request.ParametersSchemaJson;
            }

            foreach (var (value, setter, label) in new (string?, Action<string?>, string)[]
            {
                (request.QueryParamsJson, v => definition.QueryParamsJson = v, "QueryParamsJson"),
                (request.StaticHeadersJson, v => definition.StaticHeadersJson = v, "StaticHeadersJson"),
                (request.BodyTemplateJson, v => definition.BodyTemplateJson = v, "BodyTemplateJson"),
                (request.ErrorMappingJson, v => definition.ErrorMappingJson = v, "ErrorMappingJson"),
            })
            {
                if (value is null)
                    continue;
                if (!string.IsNullOrWhiteSpace(value) && !IsJsonObject(value))
                    return Results.BadRequest(new { error = $"{label} debe ser un objeto JSON." });
                setter(NullIfEmpty(value));
            }

            if (request.ResponseMappingJson is not null)
            {
                if (!string.IsNullOrWhiteSpace(request.ResponseMappingJson)
                    && !IsJsonArray(request.ResponseMappingJson))
                {
                    return Results.BadRequest(new { error = "ResponseMappingJson debe ser un arreglo JSON." });
                }

                definition.ResponseMappingJson = NullIfEmpty(request.ResponseMappingJson);
            }

            if (request.DataClassification is not null)
            {
                if (!Enum.TryParse<ToolDataClassification>(request.DataClassification, true, out var classification))
                    return Results.BadRequest(new { error = $"Clasificación inválida: {request.DataClassification}" });
                definition.DataClassification = classification;
            }

            if (request.DisplayName is not null)
            {
                if (string.IsNullOrWhiteSpace(request.DisplayName))
                    return Results.BadRequest(new { error = "El nombre para mostrar es obligatorio." });
                definition.DisplayName = request.DisplayName.Trim();
            }

            if (request.AiDescription is not null)
            {
                if (string.IsNullOrWhiteSpace(request.AiDescription))
                    return Results.BadRequest(new { error = "La descripción para la IA es obligatoria." });
                definition.AiDescription = request.AiDescription.Trim();
            }

            if (request.Category is not null)
                definition.Category = NullIfEmpty(request.Category);
            if (request.SupportContact is not null)
                definition.SupportContact = NullIfEmpty(request.SupportContact);
            if (request.AiUsageNotes is not null)
                definition.AiUsageNotes = NullIfEmpty(request.AiUsageNotes);
            if (request.NormalizeArgDiacritics is not null)
                definition.NormalizeArgDiacritics = request.NormalizeArgDiacritics.Value;
            if (request.RequiresAuthenticatedUser is not null)
                definition.RequiresAuthenticatedUser = request.RequiresAuthenticatedUser.Value;
            if (request.MaxCallsPerConversation is not null)
                definition.MaxCallsPerConversation = request.MaxCallsPerConversation.Value <= 0
                    ? null
                    : request.MaxCallsPerConversation.Value;
            if (request.TimeoutMsOverride is not null)
                definition.TimeoutMsOverride = request.TimeoutMsOverride.Value <= 0
                    ? null
                    : Math.Clamp(request.TimeoutMsOverride.Value, 500, 30000);

            await db.SaveChangesAsync();

            var botCount = await db.BotTools.CountAsync(bt => bt.ToolDefinitionId == id && bt.Enabled);
            return Results.Ok(ToResponse(definition, botCount));
        })
        .RequirePermission(Permissions.ToolUpdate)
        .WithName("UpdateToolDefinition")
        .Produces<ToolDefinitionResponse>()
        .Produces(400)
        .Produces(404)
        .Produces(409);

        group.MapDelete("/{id:int}", async (int id, BotFactoryDbContext db) =>
        {
            var definition = await db.ToolDefinitions.FirstOrDefaultAsync(d => d.Id == id);
            if (definition is null)
                return Results.NotFound();

            var dependentBots = await db.BotTools
                .Where(bt => bt.ToolDefinitionId == id)
                .Join(db.Bots, bt => bt.BotId, b => b.Id, (bt, b) => b.BotName)
                .ToListAsync();

            if (dependentBots.Count > 0)
            {
                return Results.Conflict(new
                {
                    error = "La herramienta está asociada a bots. Quita las asociaciones primero.",
                    bots = dependentBots,
                });
            }

            db.ToolDefinitions.Remove(definition);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequirePermission(Permissions.ToolDelete)
        .WithName("DeleteToolDefinition")
        .Produces(204)
        .Produces(404)
        .Produces(409);

        // Publishing IS the approval step (review decision) — gated on tool:publish.
        group.MapPost("/{id:int}/publish", async (int id, BotFactoryDbContext db) =>
        {
            var definition = await db.ToolDefinitions.FirstOrDefaultAsync(d => d.Id == id);
            if (definition is null)
                return Results.NotFound();

            // v1 is read-only: state-changing tools cannot go live.
            if (definition.IsConsequential)
            {
                return Results.BadRequest(new
                {
                    error = "Las herramientas que modifican estado no se pueden publicar en esta versión.",
                });
            }

            definition.Status = ToolDefinitionStatus.Published;
            await db.SaveChangesAsync();
            return Results.Ok(new { status = definition.Status.ToString() });
        })
        .RequirePermission(Permissions.ToolPublish)
        .WithName("PublishToolDefinition")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        group.MapPost("/{id:int}/unpublish", async (int id, BotFactoryDbContext db) =>
        {
            var definition = await db.ToolDefinitions.FirstOrDefaultAsync(d => d.Id == id);
            if (definition is null)
                return Results.NotFound();

            definition.Status = ToolDefinitionStatus.Draft;
            await db.SaveChangesAsync();
            return Results.Ok(new { status = definition.Status.ToString() });
        })
        .RequirePermission(Permissions.ToolPublish)
        .WithName("UnpublishToolDefinition")
        .Produces(200)
        .Produces(404);

        group.MapPost("/{id:int}/disable", async (int id, BotFactoryDbContext db) =>
        {
            var definition = await db.ToolDefinitions.FirstOrDefaultAsync(d => d.Id == id);
            if (definition is null)
                return Results.NotFound();

            definition.Status = ToolDefinitionStatus.Disabled;
            await db.SaveChangesAsync();
            return Results.Ok(new { status = definition.Status.ToString() });
        })
        .RequirePermission(Permissions.ToolPublish)
        .WithName("DisableToolDefinition")
        .Produces(200)
        .Produces(404);

        // Test console: run the tool through the real gateway in test mode
        // (Draft allowed, no conversation, recorded with IsTest = true).
        group.MapPost("/{id:int}/test", async (
            int id,
            ToolTestRequest request,
            BotFactoryDbContext db,
            IToolExecutionGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var definition = await db.ToolDefinitions
                .Include(d => d.Connection)
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (definition?.Connection is null)
                return Results.NotFound();

            var result = await gateway.ExecuteTestAsync(
                definition, definition.Connection, request.Arguments, cancellationToken);

            definition.LastTestedAt = DateTime.UtcNow;
            definition.LastTestOutcome = result.Outcome == ToolExecutionOutcome.Success
                ? ToolTestOutcomes.Passed
                : ToolTestOutcomes.Failed;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new ToolTestResponse(
                result.Outcome.ToString(),
                result.HttpStatus,
                result.LatencyMs,
                result.ResolvedRequestSummary,
                result.RedactedResponseJson,
                result.ErrorCategory,
                result.ErrorMessage,
                result.ValidationErrors.ToList()));
        })
        .RequirePermission(Permissions.ToolUpdate)
        .WithName("TestToolDefinition")
        .Produces<ToolTestResponse>()
        .Produces(404);

        group.MapGet("/{id:int}/executions", async (
            int id,
            BotFactoryDbContext db,
            int page = 1,
            int pageSize = 25) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.ToolExecutions
                .AsNoTracking()
                .Where(e => e.ToolDefinitionId == id)
                .OrderByDescending(e => e.CreatedAt);

            var total = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new PaginatedResponse<ToolExecutionResponse>(
                items.Select(ToExecutionResponse).ToList(),
                page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
        })
        .RequirePermission(Permissions.ToolRead)
        .WithName("GetToolExecutions")
        .Produces<PaginatedResponse<ToolExecutionResponse>>();
    }

    private static string? ValidateDefinitionFields(
        string displayName, string aiDescription, string httpMethod, string pathTemplate,
        string parametersSchemaJson, string? queryParamsJson, string? staticHeadersJson,
        string? bodyTemplateJson, string? errorMappingJson, string? dataClassificationRaw,
        out ToolDataClassification dataClassification)
    {
        dataClassification = ToolDataClassification.Internal;

        if (string.IsNullOrWhiteSpace(displayName))
            return "El nombre para mostrar es obligatorio.";
        if (string.IsNullOrWhiteSpace(aiDescription))
            return "La descripción para la IA es obligatoria.";
        if (string.IsNullOrWhiteSpace(httpMethod) || !AllowedHttpMethods.Contains(httpMethod))
            return $"Método HTTP inválido: {httpMethod}";
        if (string.IsNullOrWhiteSpace(pathTemplate))
            return "La plantilla de ruta es obligatoria.";

        var schemaError = ToolArgumentValidator.ValidateSchemaDocument(parametersSchemaJson);
        if (schemaError is not null)
            return schemaError;

        foreach (var (value, label) in new (string?, string)[]
        {
            (queryParamsJson, "QueryParamsJson"),
            (staticHeadersJson, "StaticHeadersJson"),
            (bodyTemplateJson, "BodyTemplateJson"),
            (errorMappingJson, "ErrorMappingJson"),
        })
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsJsonObject(value))
                return $"{label} debe ser un objeto JSON.";
        }

        if (dataClassificationRaw is not null
            && !Enum.TryParse(dataClassificationRaw, true, out dataClassification))
        {
            return $"Clasificación inválida: {dataClassificationRaw}";
        }

        return null;
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsJsonArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizePath(string pathTemplate)
    {
        var trimmed = pathTemplate.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ToolDefinitionResponse ToResponse(ToolDefinition d, int botCount) => new(
        d.Id, d.ToolConnectionId, d.Connection?.Name ?? string.Empty,
        d.Name, d.DisplayName, d.Category, d.SupportContact,
        d.AiDescription, d.AiUsageNotes,
        d.HttpMethod, d.PathTemplate, d.QueryParamsJson, d.StaticHeadersJson, d.BodyTemplateJson,
        d.ParametersSchemaJson, d.ResponseMappingJson, d.ErrorMappingJson,
        d.NormalizeArgDiacritics,
        d.IsConsequential, d.RequiresUserConfirmation, d.RequiresAuthenticatedUser,
        d.MaxCallsPerConversation, d.TimeoutMsOverride,
        d.DataClassification.ToString(), d.Status.ToString(),
        d.LastTestedAt, d.LastTestOutcome, botCount, d.CreatedAt, d.UpdatedAt);

    private static ToolExecutionResponse ToExecutionResponse(ToolExecution e) => new(
        e.Id, e.BotId, e.ConversationId, e.TurnId,
        e.ToolName, e.ConnectorType.ToString(), e.Outcome.ToString(),
        e.HttpStatus, e.ErrorCategory, e.ErrorMessage,
        e.ResolvedRequestSummary, e.ArgumentsJson, e.ResultSnippetJson,
        e.ResponseBytes, e.LatencyMs, e.RetryCount, e.IsTest, e.CreatedAt);
}
