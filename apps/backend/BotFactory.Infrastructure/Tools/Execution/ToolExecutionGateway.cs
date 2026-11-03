using System.Diagnostics;
using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Tools.Execution;

/// <summary>
/// The controlled pipeline for configured tools: validate args → deterministic
/// policy → resolve credentials → protocol connector → redact → record
/// ToolExecution + telemetry → structured result for the model. External
/// failures never throw: the model receives an explicit error payload and is
/// instructed (system suffix) to say the service is unavailable — never to
/// invent a result. Built-ins (kb_search) never enter this class.
/// </summary>
public class ToolExecutionGateway : IToolExecutionGateway
{
    private const int SnippetMaxChars = 4000;

    private readonly BotFactoryDbContext _db;
    private readonly IEnumerable<IToolConnector> _connectors;
    private readonly ToolArgumentValidator _validator;
    private readonly ToolPolicyEvaluator _policy;
    private readonly IToolCredentialProvider _credentials;
    private readonly ILogger<ToolExecutionGateway> _logger;

    public ToolExecutionGateway(
        BotFactoryDbContext db,
        IEnumerable<IToolConnector> connectors,
        ToolArgumentValidator validator,
        ToolPolicyEvaluator policy,
        IToolCredentialProvider credentials,
        ILogger<ToolExecutionGateway> logger)
    {
        _db = db;
        _connectors = connectors;
        _validator = validator;
        _policy = policy;
        _credentials = credentials;
        _logger = logger;
    }

    public async Task<ToolInvocation> ExecuteAsync(
        ResolvedBotTool tool,
        JsonElement args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var definition = tool.Definition;
        var connection = tool.Connection;
        var redactedArgs = ToolRedaction.RedactElement(args, SnippetMaxChars);

        // 1. Schema validation — the model's arguments must match the contract.
        var problems = _validator.Validate(definition.ParametersSchemaJson, args);
        if (problems.Count > 0)
        {
            var execution = await RecordAsync(
                definition, connection, redactedArgs,
                ToolExecutionOutcome.ValidationFailed,
                summary: $"{definition.HttpMethod} <no ejecutado>",
                errorCategory: "validation",
                errorMessage: ToolRedaction.Truncate(string.Join(" | ", problems), 2000),
                latencyMs: (int)sw.ElapsedMilliseconds,
                context: context);

            return BuildErrorInvocation(
                definition, execution, "tool_call_failed",
                new
                {
                    error = "validation_failed",
                    problems,
                    instrucciones = "Faltan datos o son inválidos. Pide al usuario la información "
                        + "necesaria antes de volver a llamar la herramienta.",
                });
        }

        // 2. Deterministic policy — the model is never the authority.
        var decision = await _policy.EvaluateAsync(tool, context, cancellationToken);
        if (!decision.Allowed)
        {
            var execution = await RecordAsync(
                definition, connection, redactedArgs,
                ToolExecutionOutcome.PolicyBlocked,
                summary: $"{definition.HttpMethod} <bloqueado>",
                errorCategory: decision.ReasonCode,
                errorMessage: decision.UserMessage,
                latencyMs: (int)sw.ElapsedMilliseconds,
                context: context);

            return BuildErrorInvocation(
                definition, execution, "tool_call_blocked",
                new
                {
                    error = "blocked",
                    reason = decision.ReasonCode,
                    userMessage = decision.UserMessage,
                });
        }

        // 3–5. Credentials + connector.
        var (result, invocationPayload) = await InvokeConnectorAsync(
            definition, connection, args, cancellationToken);

        sw.Stop();

        var record = await RecordAsync(
            definition, connection, redactedArgs,
            result.Outcome,
            summary: result.ResolvedRequestSummary,
            errorCategory: result.ErrorCategory,
            errorMessage: result.ErrorMessage,
            latencyMs: (int)sw.ElapsedMilliseconds,
            httpStatus: result.HttpStatus,
            responseBytes: result.ResponseBytes,
            retryCount: result.RetryCount,
            resultSnippet: result.Payload is { } p
                ? ToolRedaction.RedactElement(p, SnippetMaxChars)
                : null,
            context: context);

        if (result.Outcome == ToolExecutionOutcome.Success)
        {
            var snippet = ToolRedaction.RedactElement(result.Payload!.Value, SnippetMaxChars);

            return new ToolInvocation(
                ResponsePayload: invocationPayload!,
                Telemetry: BuildTelemetry("tool_call_completed", definition, record, result),
                NoUsableResults: false,
                Display:
                [
                    new ToolDisplayItem(
                        Id: record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Label: $"{definition.DisplayName} · {result.HttpStatus} · {record.LatencyMs} ms",
                        Body: snippet),
                ],
                Summary: $"{definition.Name} · success · {record.LatencyMs}ms");
        }

        return BuildErrorInvocation(
            definition, record, "tool_call_failed",
            new
            {
                error = result.ErrorCategory ?? "error",
                userMessage = result.UserFacingError
                    ?? "El servicio externo no está disponible en este momento.",
            },
            result);
    }

    public async Task<ToolTestResult> ExecuteTestAsync(
        ToolDefinition definition,
        ToolConnection connection,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var redactedArgs = ToolRedaction.RedactElement(args, SnippetMaxChars);

        var problems = _validator.Validate(definition.ParametersSchemaJson, args);
        if (problems.Count > 0)
        {
            await RecordAsync(
                definition, connection, redactedArgs,
                ToolExecutionOutcome.ValidationFailed,
                summary: $"{definition.HttpMethod} <no ejecutado>",
                errorCategory: "validation",
                errorMessage: ToolRedaction.Truncate(string.Join(" | ", problems), 2000),
                latencyMs: (int)sw.ElapsedMilliseconds,
                isTest: true);

            return new ToolTestResult(
                ToolExecutionOutcome.ValidationFailed, null, (int)sw.ElapsedMilliseconds,
                $"{definition.HttpMethod} <no ejecutado>", null, "validation",
                "Los argumentos no cumplen el esquema declarado.", problems);
        }

        // Test mode skips bot policy (no conversation, Draft allowed) but the
        // read-only rule still holds: consequential tools never execute in v1.
        if (definition.IsConsequential)
        {
            return new ToolTestResult(
                ToolExecutionOutcome.PolicyBlocked, null, 0,
                $"{definition.HttpMethod} <bloqueado>", null, "consequential_not_supported",
                "Las herramientas que modifican estado no están habilitadas en esta versión.", []);
        }

        var (result, _) = await InvokeConnectorAsync(definition, connection, args, cancellationToken);
        sw.Stop();

        await RecordAsync(
            definition, connection, redactedArgs,
            result.Outcome,
            summary: result.ResolvedRequestSummary,
            errorCategory: result.ErrorCategory,
            errorMessage: result.ErrorMessage,
            latencyMs: (int)sw.ElapsedMilliseconds,
            httpStatus: result.HttpStatus,
            responseBytes: result.ResponseBytes,
            retryCount: result.RetryCount,
            resultSnippet: result.Payload is { } p
                ? ToolRedaction.RedactElement(p, SnippetMaxChars)
                : null,
            isTest: true);

        return new ToolTestResult(
            result.Outcome,
            result.HttpStatus,
            (int)sw.ElapsedMilliseconds,
            result.ResolvedRequestSummary,
            result.Payload is { } payload
                ? ToolRedaction.RedactElement(payload, SnippetMaxChars)
                : null,
            result.ErrorCategory,
            result.ErrorMessage,
            []);
    }

    private async Task<(ToolConnectorResult Result, object? SuccessPayload)> InvokeConnectorAsync(
        ToolDefinition definition,
        ToolConnection connection,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var connector = _connectors.FirstOrDefault(c => c.Type == connection.ConnectorType);
        if (connector is null)
        {
            return (new ToolConnectorResult(
                ToolExecutionOutcome.ConnectorError, null, null, 0, 0,
                $"{definition.HttpMethod} <sin conector>",
                ErrorCategory: "connector_missing",
                ErrorMessage: $"No hay conector para el tipo '{connection.ConnectorType}'.",
                UserFacingError: "El servicio externo no está disponible en este momento."), null);
        }

        ToolCredentials credentials;
        try
        {
            credentials = _credentials.Resolve(connection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve credentials for connection {ConnectionId}", connection.Id);
            return (new ToolConnectorResult(
                ToolExecutionOutcome.ConnectorError, null, null, 0, 0,
                $"{definition.HttpMethod} <credenciales>",
                ErrorCategory: "credentials_error",
                ErrorMessage: "No se pudieron descifrar las credenciales de la conexión.",
                UserFacingError: "El servicio externo no está disponible en este momento."), null);
        }

        var result = await connector.InvokeAsync(
            new ToolConnectorRequest(definition, connection, args, credentials),
            cancellationToken);

        object? successPayload = result.Outcome == ToolExecutionOutcome.Success
            ? result.Payload!.Value
            : null;

        return (result, successPayload);
    }

    private async Task<ToolExecution> RecordAsync(
        ToolDefinition definition,
        ToolConnection connection,
        string redactedArgs,
        ToolExecutionOutcome outcome,
        string summary,
        string? errorCategory,
        string? errorMessage,
        int latencyMs,
        int? httpStatus = null,
        long responseBytes = 0,
        int retryCount = 0,
        string? resultSnippet = null,
        ToolContext? context = null,
        bool isTest = false)
    {
        var execution = new ToolExecution
        {
            BotId = context?.Bot.Id,
            ConversationId = context?.Conversation.Id,
            TurnId = context?.TurnId,
            UserId = context?.Conversation.UserId,
            ToolDefinitionId = definition.Id,
            ToolConnectionId = connection.Id,
            ToolName = definition.Name,
            ConnectorType = connection.ConnectorType,
            ArgumentsJson = redactedArgs,
            Outcome = outcome,
            HttpStatus = httpStatus,
            ErrorCategory = errorCategory,
            ErrorMessage = errorMessage is null ? null : ToolRedaction.Truncate(errorMessage, 2000),
            ResolvedRequestSummary = ToolRedaction.Truncate(summary, 1000),
            ResponseBytes = responseBytes,
            LatencyMs = latencyMs,
            RetryCount = retryCount,
            ResultSnippetJson = resultSnippet,
            DefinitionSnapshotJson = BuildSnapshot(definition, connection),
            IsTest = isTest,
        };

        _db.ToolExecutions.Add(execution);

        // Best-effort persistence on the cancellation path; the record itself
        // must survive even when the turn was aborted.
        await _db.SaveChangesAsync(CancellationToken.None);
        return execution;
    }

    private static ToolInvocation BuildErrorInvocation(
        ToolDefinition definition,
        ToolExecution execution,
        string eventName,
        object errorPayload,
        ToolConnectorResult? result = null)
    {
        return new ToolInvocation(
            ResponsePayload: errorPayload,
            Telemetry: BuildTelemetry(eventName, definition, execution, result),
            NoUsableResults: false,
            Display:
            [
                new ToolDisplayItem(
                    Id: execution.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Label: $"{definition.DisplayName} · {execution.Outcome}",
                    Body: execution.ErrorMessage ?? execution.ErrorCategory ?? "error"),
            ],
            Summary: $"{definition.Name} · {execution.Outcome} · {execution.LatencyMs}ms");
    }

    private static ToolTelemetry BuildTelemetry(
        string eventName,
        ToolDefinition definition,
        ToolExecution execution,
        ToolConnectorResult? result)
    {
        return new ToolTelemetry(
            EventName: eventName,
            Stage: "tool",
            Data: new
            {
                tool = definition.Name,
                displayName = definition.DisplayName,
                outcome = execution.Outcome.ToString(),
                httpStatus = execution.HttpStatus,
                latencyMs = execution.LatencyMs,
                responseBytes = execution.ResponseBytes,
                retryCount = execution.RetryCount,
                errorCategory = execution.ErrorCategory,
                requestSummary = execution.ResolvedRequestSummary,
                executionId = execution.Id,
            });
    }

    private static string BuildSnapshot(ToolDefinition definition, ToolConnection connection)
    {
        // Exact configuration used, secrets stripped — traceability without a
        // version entity (plan §5.5).
        return JsonSerializer.Serialize(new
        {
            tool = new
            {
                definition.Id,
                definition.Name,
                definition.DisplayName,
                definition.HttpMethod,
                definition.PathTemplate,
                definition.QueryParamsJson,
                definition.StaticHeadersJson,
                definition.BodyTemplateJson,
                definition.ParametersSchemaJson,
                definition.ResponseMappingJson,
                definition.ErrorMappingJson,
                definition.NormalizeArgDiacritics,
                definition.TimeoutMsOverride,
                definition.MaxCallsPerConversation,
                definition.RequiresAuthenticatedUser,
                Status = definition.Status.ToString(),
                definition.UpdatedAt,
            },
            connection = new
            {
                connection.Id,
                connection.Name,
                ConnectorType = connection.ConnectorType.ToString(),
                connection.BaseUrl,
                AuthType = connection.AuthType.ToString(),
                connection.AuthConfigJson,
                connection.TimeoutMs,
                connection.MaxResponseBytes,
                Status = connection.Status.ToString(),
                connection.UpdatedAt,
            },
        });
    }
}
