namespace BotFactory.Core.Entities;

/// <summary>
/// Protocol adapter used to reach the external system. Stored as string;
/// adding a value here requires a matching IToolConnector implementation.
/// </summary>
public enum ToolConnectorType
{
    Rest,
}

/// <summary>
/// How the connector authenticates against the external system. Non-secret
/// parts (header name, username) live in ToolConnection.AuthConfigJson; secret
/// values live encrypted in ToolConnection.EncryptedSecretsJson.
/// </summary>
public enum ToolAuthType
{
    None,
    ApiKeyHeader,
    Bearer,
    Basic,

    /// <summary>
    /// API key as a query-string parameter (e.g. Weatherstack's access_key).
    /// Param name comes from AuthConfigJson.paramName; the value is appended to
    /// the request URI only after the redacted request summary is built, so the
    /// secret never reaches execution records or logs.
    /// </summary>
    ApiKeyQuery,
}

public enum ToolConnectionStatus
{
    Active,
    /// <summary>Emergency kill switch: no tool on this connection executes.</summary>
    Disabled,
}

/// <summary>
/// Tool lifecycle. Publishing requires tool:publish and IS the approval step
/// (review decision — no separate pending_approval state).
/// </summary>
public enum ToolDefinitionStatus
{
    Draft,
    Published,
    Disabled,
    Archived,
}

/// <summary>
/// Sensitivity of the data the tool touches. Informational in v1 (shown in the
/// admin UI, recorded on executions); policy rules can hang off it later.
/// </summary>
public enum ToolDataClassification
{
    Public,
    Internal,
    Personal,
    Sensitive,
}

/// <summary>
/// Terminal outcome of one tool execution attempt. ConfirmationPending is
/// reserved for the Phase 4 consequential-tools flow.
/// </summary>
public enum ToolExecutionOutcome
{
    Success,
    ValidationFailed,
    PolicyBlocked,
    ConfirmationPending,
    Timeout,
    HttpError,
    ConnectorError,
    Cancelled,
}

/// <summary>Canonical values for ToolDefinition.LastTestOutcome.</summary>
public static class ToolTestOutcomes
{
    public const string Passed = "passed";
    public const string Failed = "failed";
}
