namespace BotFactory.Core.DTOs;

/// <summary>Secrets are write-only: responses expose only HasSecrets + SecretPrefix.</summary>
public record ToolConnectionResponse(
    int Id,
    string Name,
    string? Description,
    string ConnectorType,
    string BaseUrl,
    string AuthType,
    string AuthConfigJson,
    bool HasSecrets,
    string SecretPrefix,
    string? DefaultHeadersJson,
    int TimeoutMs,
    int MaxResponseBytes,
    string Status,
    int ToolCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateToolConnectionRequest(
    string Name,
    string? Description,
    string ConnectorType,
    string BaseUrl,
    string AuthType,
    string? AuthConfigJson,
    string? SecretsJson,
    string? DefaultHeadersJson,
    int? TimeoutMs,
    int? MaxResponseBytes);

/// <summary>Null = leave unchanged. SecretsJson null keeps stored secrets; empty string clears them.</summary>
public record UpdateToolConnectionRequest(
    string? Name,
    string? Description,
    string? BaseUrl,
    string? AuthType,
    string? AuthConfigJson,
    string? SecretsJson,
    string? DefaultHeadersJson,
    int? TimeoutMs,
    int? MaxResponseBytes,
    string? Status);

public record ToolConnectionProbeResponse(
    bool Ok,
    int? HttpStatus,
    int LatencyMs,
    string? Error);
