namespace BotFactory.Core.Entities;

/// <summary>
/// Reusable technical configuration for one external system (e.g. "Registro
/// Civil — producción"). Institution-scoped via BaseEntity; many
/// ToolDefinitions can share one connection. Credentials are stored encrypted
/// and never leave the backend (see planning/external-tools-plan.md §5.1).
/// </summary>
public class ToolConnection : BaseEntity, IAuditable
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ToolConnectorType ConnectorType { get; set; } = ToolConnectorType.Rest;

    /// <summary>
    /// Scheme + host (+ optional base path) every tool on this connection is
    /// pinned to. Tool path templates are resolved relative to it and the
    /// egress guard rejects any resolved URL that escapes this host.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public ToolAuthType AuthType { get; set; } = ToolAuthType.None;

    /// <summary>
    /// Non-secret auth configuration as JSON, e.g. {"headerName":"X-Api-Key"}
    /// for ApiKeyHeader or {"username":"svc-bots"} for Basic.
    /// </summary>
    public string AuthConfigJson { get; set; } = "{}";

    /// <summary>
    /// AES-encrypted JSON bundle of secret values, e.g. {"apiKey":"…"} or
    /// {"password":"…"}. Decrypted only inside the execution gateway at call
    /// time. Never serialized into DTOs, logs, or room metadata.
    /// </summary>
    [AuditMask]
    public string EncryptedSecretsJson { get; set; } = string.Empty;

    /// <summary>First characters of the primary secret, for masked display.</summary>
    public string SecretPrefix { get; set; } = string.Empty;

    /// <summary>Optional static extra headers (non-secret) as a JSON object.</summary>
    public string? DefaultHeadersJson { get; set; }

    /// <summary>Per-call budget. Voice-enabled bots should stay well under the agent's 30 s SSE read timeout.</summary>
    public int TimeoutMs { get; set; } = 8000;

    public int MaxResponseBytes { get; set; } = 262144;

    public ToolConnectionStatus Status { get; set; } = ToolConnectionStatus.Active;
}
