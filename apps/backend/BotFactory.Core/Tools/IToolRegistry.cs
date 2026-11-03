namespace BotFactory.Core.Tools;

/// <summary>
/// Outer wrapper for Gemini's <c>tools</c> array. Each entry contains a group
/// of function declarations; we emit a single group with all registered tools.
/// </summary>
public sealed record GeminiToolGroup
{
    public required IReadOnlyList<FunctionDeclaration> FunctionDeclarations { get; init; }
}

public interface IToolRegistry
{
    /// <summary>
    /// Returns the Gemini-shaped <c>tools</c> array, ready to inline into a
    /// streamGenerateContent request body. Empty array if no handlers registered.
    /// </summary>
    GeminiToolGroup[] BuildGeminiToolsPayload();

    IToolHandler? Get(string name);
}
