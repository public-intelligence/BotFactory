namespace BotFactory.Core.Tools;

/// <summary>
/// JSON-Schema-shaped definition of a function the model can call. Wire format
/// for Gemini's <c>functionDeclarations</c> array; OpenAI/Anthropic accept the
/// same shape with minor key renames, so this record is provider-agnostic.
/// </summary>
public sealed record FunctionDeclaration
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonSchema Parameters { get; init; }
}

/// <summary>
/// Minimal JSON Schema record covering the subset Gemini accepts in tool
/// parameters. Properties not used by a given schema stay null and are omitted
/// from the wire payload by the configured serializer.
/// </summary>
public sealed record JsonSchema
{
    public required string Type { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, JsonSchema>? Properties { get; init; }
    public IReadOnlyList<string>? Required { get; init; }
    public JsonSchema? Items { get; init; }
    public IReadOnlyList<string>? Enum { get; init; }
}
