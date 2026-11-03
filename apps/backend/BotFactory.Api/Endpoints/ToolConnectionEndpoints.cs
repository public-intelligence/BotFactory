using System.Diagnostics;
using System.Text.Json;
using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Api.Endpoints;

public static class ToolConnectionEndpoints
{
    public static void MapToolConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tool-connections")
            .RequireAuthorization()
            .WithTags("Tool Connections");

        group.MapGet("/", async (BotFactoryDbContext db) =>
        {
            var connections = await db.ToolConnections
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    Connection = c,
                    ToolCount = db.ToolDefinitions.Count(d => d.ToolConnectionId == c.Id),
                })
                .ToListAsync();

            return Results.Ok(connections.Select(x => ToResponse(x.Connection, x.ToolCount)).ToList());
        })
        .RequirePermission(Permissions.ToolRead)
        .WithName("GetToolConnections")
        .Produces<List<ToolConnectionResponse>>();

        group.MapGet("/{id:int}", async (int id, BotFactoryDbContext db) =>
        {
            var connection = await db.ToolConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (connection is null)
                return Results.NotFound();

            var toolCount = await db.ToolDefinitions.CountAsync(d => d.ToolConnectionId == id);
            return Results.Ok(ToResponse(connection, toolCount));
        })
        .RequirePermission(Permissions.ToolRead)
        .WithName("GetToolConnection")
        .Produces<ToolConnectionResponse>()
        .Produces(404);

        group.MapPost("/", async (
            CreateToolConnectionRequest request,
            BotFactoryDbContext db,
            IEncryptionService encryption) =>
        {
            var error = ValidateConnectionBasics(request.Name, request.BaseUrl, request.ConnectorType,
                request.AuthType, request.AuthConfigJson, request.DefaultHeadersJson,
                out var connectorType, out var authType);
            if (error is not null)
                return Results.BadRequest(new { error });

            if (await db.ToolConnections.AnyAsync(c => c.Name == request.Name))
                return Results.Conflict(new { error = $"Ya existe una conexión llamada '{request.Name}'." });

            var (encryptedSecrets, secretPrefix, secretsError) =
                EncryptSecrets(request.SecretsJson, encryption);
            if (secretsError is not null)
                return Results.BadRequest(new { error = secretsError });

            var connection = new ToolConnection
            {
                Name = request.Name.Trim(),
                Description = request.Description,
                ConnectorType = connectorType,
                BaseUrl = request.BaseUrl.TrimEnd('/'),
                AuthType = authType,
                AuthConfigJson = string.IsNullOrWhiteSpace(request.AuthConfigJson) ? "{}" : request.AuthConfigJson,
                EncryptedSecretsJson = encryptedSecrets,
                SecretPrefix = secretPrefix,
                DefaultHeadersJson = request.DefaultHeadersJson,
                TimeoutMs = Math.Clamp(request.TimeoutMs ?? 8000, 500, 30000),
                MaxResponseBytes = Math.Clamp(request.MaxResponseBytes ?? 262144, 1024, 4 * 1024 * 1024),
            };

            db.ToolConnections.Add(connection);
            await db.SaveChangesAsync();

            return Results.Created($"/api/tool-connections/{connection.Id}", ToResponse(connection, 0));
        })
        .RequirePermission(Permissions.ToolUpdate)
        .WithName("CreateToolConnection")
        .Produces<ToolConnectionResponse>(201)
        .Produces(400)
        .Produces(409);

        group.MapPut("/{id:int}", async (
            int id,
            UpdateToolConnectionRequest request,
            BotFactoryDbContext db,
            IEncryptionService encryption) =>
        {
            var connection = await db.ToolConnections.FirstOrDefaultAsync(c => c.Id == id);
            if (connection is null)
                return Results.NotFound();

            if (request.Name is not null && request.Name != connection.Name)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest(new { error = "El nombre es obligatorio." });
                if (await db.ToolConnections.AnyAsync(c => c.Name == request.Name && c.Id != id))
                    return Results.Conflict(new { error = $"Ya existe una conexión llamada '{request.Name}'." });
                connection.Name = request.Name.Trim();
            }

            if (request.BaseUrl is not null)
            {
                if (!IsValidBaseUrl(request.BaseUrl))
                    return Results.BadRequest(new { error = "La URL base debe ser una URL http(s) absoluta." });
                connection.BaseUrl = request.BaseUrl.TrimEnd('/');
            }

            if (request.AuthType is not null)
            {
                if (!Enum.TryParse<ToolAuthType>(request.AuthType, true, out var authType))
                    return Results.BadRequest(new { error = $"Tipo de autenticación inválido: {request.AuthType}" });
                connection.AuthType = authType;
            }

            if (request.AuthConfigJson is not null)
            {
                if (!IsJsonObjectOrEmpty(request.AuthConfigJson))
                    return Results.BadRequest(new { error = "AuthConfigJson debe ser un objeto JSON." });
                connection.AuthConfigJson = string.IsNullOrWhiteSpace(request.AuthConfigJson) ? "{}" : request.AuthConfigJson;
            }

            if (request.DefaultHeadersJson is not null)
            {
                if (!IsJsonObjectOrEmpty(request.DefaultHeadersJson))
                    return Results.BadRequest(new { error = "DefaultHeadersJson debe ser un objeto JSON." });
                connection.DefaultHeadersJson = string.IsNullOrWhiteSpace(request.DefaultHeadersJson) ? null : request.DefaultHeadersJson;
            }

            if (request.SecretsJson is not null)
            {
                // Empty string clears the stored secrets; non-empty replaces them.
                var (encryptedSecrets, secretPrefix, secretsError) =
                    EncryptSecrets(request.SecretsJson, encryption);
                if (secretsError is not null)
                    return Results.BadRequest(new { error = secretsError });
                connection.EncryptedSecretsJson = encryptedSecrets;
                connection.SecretPrefix = secretPrefix;
            }

            if (request.Description is not null)
                connection.Description = request.Description.Length == 0 ? null : request.Description;
            if (request.TimeoutMs is not null)
                connection.TimeoutMs = Math.Clamp(request.TimeoutMs.Value, 500, 30000);
            if (request.MaxResponseBytes is not null)
                connection.MaxResponseBytes = Math.Clamp(request.MaxResponseBytes.Value, 1024, 4 * 1024 * 1024);

            if (request.Status is not null)
            {
                if (!Enum.TryParse<ToolConnectionStatus>(request.Status, true, out var status))
                    return Results.BadRequest(new { error = $"Estado inválido: {request.Status}" });
                connection.Status = status;
            }

            await db.SaveChangesAsync();

            var toolCount = await db.ToolDefinitions.CountAsync(d => d.ToolConnectionId == id);
            return Results.Ok(ToResponse(connection, toolCount));
        })
        .RequirePermission(Permissions.ToolUpdate)
        .WithName("UpdateToolConnection")
        .Produces<ToolConnectionResponse>()
        .Produces(400)
        .Produces(404)
        .Produces(409);

        group.MapDelete("/{id:int}", async (int id, BotFactoryDbContext db) =>
        {
            var connection = await db.ToolConnections.FirstOrDefaultAsync(c => c.Id == id);
            if (connection is null)
                return Results.NotFound();

            var dependentTools = await db.ToolDefinitions
                .Where(d => d.ToolConnectionId == id)
                .Select(d => d.DisplayName)
                .ToListAsync();

            if (dependentTools.Count > 0)
            {
                return Results.Conflict(new
                {
                    error = "La conexión tiene herramientas asociadas. Elimínalas primero.",
                    tools = dependentTools,
                });
            }

            db.ToolConnections.Remove(connection);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequirePermission(Permissions.ToolDelete)
        .WithName("DeleteToolConnection")
        .Produces(204)
        .Produces(404)
        .Produces(409);

        // Connectivity + auth probe: GET on the connection base URL through the
        // real connector (egress guard, auth headers, timeout) — no tool needed.
        group.MapPost("/{id:int}/probe", async (
            int id,
            BotFactoryDbContext db,
            IEnumerable<IToolConnector> connectors,
            IToolCredentialProvider credentials) =>
        {
            var connection = await db.ToolConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (connection is null)
                return Results.NotFound();

            var connector = connectors.FirstOrDefault(c => c.Type == connection.ConnectorType);
            if (connector is null)
                return Results.Ok(new ToolConnectionProbeResponse(false, null, 0,
                    $"No hay conector para el tipo '{connection.ConnectorType}'."));

            var probeDefinition = new ToolDefinition
            {
                Name = "probe",
                DisplayName = "Probe",
                HttpMethod = "GET",
                PathTemplate = "/",
                ParametersSchemaJson = """{"type":"object","properties":{}}""",
            };

            var sw = Stopwatch.StartNew();
            using var emptyArgs = JsonDocument.Parse("{}");

            ToolConnectorResult result;
            try
            {
                result = await connector.InvokeAsync(
                    new ToolConnectorRequest(
                        probeDefinition, connection, emptyArgs.RootElement,
                        credentials.Resolve(connection)),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                return Results.Ok(new ToolConnectionProbeResponse(
                    false, null, (int)sw.ElapsedMilliseconds, ex.Message));
            }

            // Any HTTP answer (even 404 on "/") proves connectivity; auth
            // failures surface as 401/403 for the admin to see.
            var reachable = result.HttpStatus is not null;
            return Results.Ok(new ToolConnectionProbeResponse(
                reachable,
                result.HttpStatus,
                (int)sw.ElapsedMilliseconds,
                reachable ? null : result.ErrorMessage));
        })
        .RequirePermission(Permissions.ToolUpdate)
        .WithName("ProbeToolConnection")
        .Produces<ToolConnectionProbeResponse>()
        .Produces(404);
    }

    private static string? ValidateConnectionBasics(
        string name, string baseUrl, string connectorTypeRaw, string authTypeRaw,
        string? authConfigJson, string? defaultHeadersJson,
        out ToolConnectorType connectorType, out ToolAuthType authType)
    {
        connectorType = ToolConnectorType.Rest;
        authType = ToolAuthType.None;

        if (string.IsNullOrWhiteSpace(name))
            return "El nombre es obligatorio.";
        if (!Enum.TryParse(connectorTypeRaw, true, out connectorType))
            return $"Tipo de conector inválido: {connectorTypeRaw}";
        if (!IsValidBaseUrl(baseUrl))
            return "La URL base debe ser una URL http(s) absoluta.";
        if (!Enum.TryParse(authTypeRaw, true, out authType))
            return $"Tipo de autenticación inválido: {authTypeRaw}";
        if (authConfigJson is not null && !IsJsonObjectOrEmpty(authConfigJson))
            return "AuthConfigJson debe ser un objeto JSON.";
        if (defaultHeadersJson is not null && !IsJsonObjectOrEmpty(defaultHeadersJson))
            return "DefaultHeadersJson debe ser un objeto JSON.";

        return null;
    }

    private static bool IsValidBaseUrl(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);

    private static bool IsJsonObjectOrEmpty(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return true;

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

    /// <summary>Parses a {"key":"secret"} bundle, encrypts it, and derives the masked prefix.</summary>
    private static (string Encrypted, string Prefix, string? Error) EncryptSecrets(
        string? secretsJson, IEncryptionService encryption)
    {
        if (string.IsNullOrWhiteSpace(secretsJson))
            return (string.Empty, string.Empty, null);

        Dictionary<string, string>? secrets;
        try
        {
            secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(secretsJson);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty,
                "SecretsJson debe ser un objeto JSON de pares clave → valor (texto).");
        }

        if (secrets is null || secrets.Count == 0)
            return (string.Empty, string.Empty, null);

        var firstValue = secrets.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;
        var prefix = firstValue.Length <= 5 ? firstValue : firstValue[..5];

        return (encryption.Encrypt(JsonSerializer.Serialize(secrets)), prefix, null);
    }

    private static ToolConnectionResponse ToResponse(ToolConnection c, int toolCount) => new(
        c.Id, c.Name, c.Description,
        c.ConnectorType.ToString(), c.BaseUrl,
        c.AuthType.ToString(), c.AuthConfigJson,
        !string.IsNullOrEmpty(c.EncryptedSecretsJson), c.SecretPrefix,
        c.DefaultHeadersJson, c.TimeoutMs, c.MaxResponseBytes,
        c.Status.ToString(), toolCount, c.CreatedAt, c.UpdatedAt);
}
