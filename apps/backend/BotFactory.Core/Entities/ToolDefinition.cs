namespace BotFactory.Core.Entities;

/// <summary>
/// One logical capability exposed to the LLM (one tool = one operation in v1).
/// References a ToolConnection for transport/credentials. The Name becomes the
/// Gemini function name; built-in handler names (kb_search, …) are reserved and
/// rejected at save time (planning/external-tools-plan.md §4.5).
/// </summary>
public class ToolDefinition : BaseEntity, IAuditable
{
    public int ToolConnectionId { get; set; }
    public ToolConnection? Connection { get; set; }

    /// <summary>LLM function name. snake_case, unique per organization, never a built-in name.</summary>
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? SupportContact { get; set; }

    /// <summary>What the tool does and when to use it — becomes the function declaration description.</summary>
    public string AiDescription { get; set; } = string.Empty;

    /// <summary>
    /// When NOT to use it, prerequisites, how to present results. Appended to
    /// the system instruction while the tool is active on a bot.
    /// </summary>
    public string? AiUsageNotes { get; set; }

    // ── Operation (REST v1) ────────────────────────────────────────────────
    public string HttpMethod { get; set; } = "GET";

    /// <summary>Path relative to the connection BaseUrl, with {param} placeholders, e.g. "/expedientes/{numero}".</summary>
    public string PathTemplate { get; set; } = string.Empty;

    /// <summary>JSON object: query name → literal or "{param}" reference.</summary>
    public string? QueryParamsJson { get; set; }

    /// <summary>JSON object of static (non-secret) headers for this operation.</summary>
    public string? StaticHeadersJson { get; set; }

    /// <summary>JSON body template with {param} placeholders in string values (POST/PUT only).</summary>
    public string? BodyTemplateJson { get; set; }

    /// <summary>
    /// JSON Schema (object root) for the tool arguments. Single source of
    /// truth for the model declaration, server-side validation, and the admin
    /// test-console form.
    /// </summary>
    public string ParametersSchemaJson { get; set; } = """{"type":"object","properties":{}}""";

    /// <summary>Optional response projection: JSON array of {"path":"a.b.c","name":"alias"} picks. Empty = pass-through (size-capped).</summary>
    public string? ResponseMappingJson { get; set; }

    /// <summary>
    /// Strip diacritics from string argument values (á→a, ñ→n, ü→u) before
    /// substitution into path/query/body. For APIs that geocode or search
    /// better on unaccented input (e.g. weatherstack). Default off.
    /// </summary>
    public bool NormalizeArgDiacritics { get; set; }

    /// <summary>Optional HTTP status → Spanish user-facing meaning, e.g. {"404":"No se encontró el expediente."}.</summary>
    public string? ErrorMappingJson { get; set; }

    // ── Policy ─────────────────────────────────────────────────────────────
    /// <summary>State-changing operations. v1 is read-only: consequential tools cannot be published yet.</summary>
    public bool IsConsequential { get; set; }

    public bool RequiresUserConfirmation { get; set; }

    /// <summary>
    /// When true (default), the tool is blocked for anonymous (public-access)
    /// conversations — evaluated against Conversation.AccessMode at execution
    /// time. Opt out only for genuinely public data (e.g. weather).
    /// </summary>
    public bool RequiresAuthenticatedUser { get; set; } = true;

    public int? MaxCallsPerConversation { get; set; }
    public int? TimeoutMsOverride { get; set; }
    public ToolDataClassification DataClassification { get; set; } = ToolDataClassification.Internal;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    public ToolDefinitionStatus Status { get; set; } = ToolDefinitionStatus.Draft;
    public DateTime? LastTestedAt { get; set; }
    public string? LastTestOutcome { get; set; }
}
