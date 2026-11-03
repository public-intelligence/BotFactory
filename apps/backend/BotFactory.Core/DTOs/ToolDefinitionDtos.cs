using System.Text.Json;

namespace BotFactory.Core.DTOs;

public record ToolDefinitionResponse(
    int Id,
    int ToolConnectionId,
    string ConnectionName,
    string Name,
    string DisplayName,
    string? Category,
    string? SupportContact,
    string AiDescription,
    string? AiUsageNotes,
    string HttpMethod,
    string PathTemplate,
    string? QueryParamsJson,
    string? StaticHeadersJson,
    string? BodyTemplateJson,
    string ParametersSchemaJson,
    string? ResponseMappingJson,
    string? ErrorMappingJson,
    bool NormalizeArgDiacritics,
    bool IsConsequential,
    bool RequiresUserConfirmation,
    bool RequiresAuthenticatedUser,
    int? MaxCallsPerConversation,
    int? TimeoutMsOverride,
    string DataClassification,
    string Status,
    DateTime? LastTestedAt,
    string? LastTestOutcome,
    int BotCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateToolDefinitionRequest(
    int ToolConnectionId,
    string Name,
    string DisplayName,
    string? Category,
    string? SupportContact,
    string AiDescription,
    string? AiUsageNotes,
    string HttpMethod,
    string PathTemplate,
    string? QueryParamsJson,
    string? StaticHeadersJson,
    string? BodyTemplateJson,
    string ParametersSchemaJson,
    string? ResponseMappingJson,
    string? ErrorMappingJson,
    bool? NormalizeArgDiacritics,
    bool? RequiresAuthenticatedUser,
    int? MaxCallsPerConversation,
    int? TimeoutMsOverride,
    string? DataClassification);

/// <summary>Null = leave unchanged.</summary>
public record UpdateToolDefinitionRequest(
    int? ToolConnectionId,
    string? Name,
    string? DisplayName,
    string? Category,
    string? SupportContact,
    string? AiDescription,
    string? AiUsageNotes,
    string? HttpMethod,
    string? PathTemplate,
    string? QueryParamsJson,
    string? StaticHeadersJson,
    string? BodyTemplateJson,
    string? ParametersSchemaJson,
    string? ResponseMappingJson,
    string? ErrorMappingJson,
    bool? NormalizeArgDiacritics,
    bool? RequiresAuthenticatedUser,
    int? MaxCallsPerConversation,
    int? TimeoutMsOverride,
    string? DataClassification);

public record ToolTestRequest(JsonElement Arguments);

public record ToolTestResponse(
    string Outcome,
    int? HttpStatus,
    int LatencyMs,
    string ResolvedRequestSummary,
    string? RedactedResponseJson,
    string? ErrorCategory,
    string? ErrorMessage,
    List<string> ValidationErrors);
