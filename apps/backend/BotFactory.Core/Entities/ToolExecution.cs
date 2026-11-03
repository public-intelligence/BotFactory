namespace BotFactory.Core.Entities;

/// <summary>
/// Governance record for one tool execution attempt (including blocked and
/// failed ones). Append-only, telemetry-style: no FK constraints so history
/// survives tool/bot deletion; ToolName/ConnectorType are denormalized for
/// display. Payload fields are stored redacted and truncated — never raw.
/// </summary>
public class ToolExecution : BaseEntity
{
    /// <summary>Null for admin test-console runs (IsTest = true).</summary>
    public int? BotId { get; set; }

    /// <summary>Null for admin test-console runs (IsTest = true).</summary>
    public int? ConversationId { get; set; }

    /// <summary>Correlates with TelemetryEvent.TurnId for the same conversational turn.</summary>
    public Guid? TurnId { get; set; }

    public string? UserId { get; set; }

    public int ToolDefinitionId { get; set; }
    public int ToolConnectionId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public ToolConnectorType ConnectorType { get; set; }

    /// <summary>Validated + redacted arguments the model proposed.</summary>
    public string ArgumentsJson { get; set; } = "{}";

    public ToolExecutionOutcome Outcome { get; set; }
    public int? HttpStatus { get; set; }

    /// <summary>Stable machine category (e.g. validation, policy_auth, timeout, dns_blocked).</summary>
    public string? ErrorCategory { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Method + redacted resolved URL after templating, e.g. "GET https://api.example.gob.do/exp/123".</summary>
    public string ResolvedRequestSummary { get; set; } = string.Empty;

    public long? ResponseBytes { get; set; }
    public int LatencyMs { get; set; }
    public int RetryCount { get; set; }

    /// <summary>Redacted, truncated response (or error payload) snippet shown in admin views.</summary>
    public string? ResultSnippetJson { get; set; }

    /// <summary>Exact tool + connection configuration used (secrets stripped) — traceability without versioning.</summary>
    public string DefinitionSnapshotJson { get; set; } = "{}";

    /// <summary>True for admin test-console executions (no conversation, draft allowed).</summary>
    public bool IsTest { get; set; }
}
