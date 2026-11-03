using System.Text.Json;
using BotFactory.Core.Entities;

namespace BotFactory.Core.Tools;

/// <summary>
/// Protocol adapter. Receives an already-validated argument set plus the
/// connection/definition config and performs the actual external call. One
/// implementation per ToolConnectorType (REST in v1; SOAP etc. later). The
/// model never constructs requests — connectors substitute validated values
/// into fixed templates only.
/// </summary>
public interface IToolConnector
{
    ToolConnectorType Type { get; }

    Task<ToolConnectorResult> InvokeAsync(
        ToolConnectorRequest request,
        CancellationToken cancellationToken);
}

public record ToolConnectorRequest(
    ToolDefinition Definition,
    ToolConnection Connection,
    JsonElement Args,
    ToolCredentials Credentials);

/// <summary>
/// Normalized connector outcome. Success = the external system answered with a
/// 2xx and a parseable (size-capped) payload; everything else carries an
/// outcome + category the gateway records verbatim.
/// </summary>
public record ToolConnectorResult(
    ToolExecutionOutcome Outcome,
    int? HttpStatus,
    JsonElement? Payload,
    long ResponseBytes,
    int RetryCount,
    string ResolvedRequestSummary,
    string? ErrorCategory = null,
    string? ErrorMessage = null,
    string? UserFacingError = null);
