namespace BotFactory.Core.Services;

/// <summary>
/// Provider-neutral tool-use policy for one model call. Gemini maps it to
/// <c>functionCallingConfig.mode</c>: <see cref="Auto"/> = AUTO (the model decides),
/// <see cref="None"/> = NONE (text only, no tool calls), <see cref="Required"/> = ANY
/// (the model must call a tool).
/// </summary>
public enum ToolChoice
{
    Auto,
    None,
    Required,
}
