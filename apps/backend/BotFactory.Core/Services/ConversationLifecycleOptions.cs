namespace BotFactory.Core.Services;

/// <summary>
/// When an idle conversation is closed (bound from the "ConversationLifecycle" section).
/// </summary>
/// <remarks>
/// A voice conversation closes after twice the bot's no-speech timeout without a message:
/// a call whose agent vanished has to be released quickly. A person typing in the chat has
/// no such timeout: they read, think and write for longer than 30 seconds, so a text
/// conversation closes only after <see cref="TextIdleCloseMinutes"/> without any message.
/// </remarks>
public class ConversationLifecycleOptions
{
    public const string SectionName = "ConversationLifecycle";

    /// <summary>Minutes without a message after which a text conversation is closed.</summary>
    public int TextIdleCloseMinutes { get; set; } = 15;

    /// <summary>
    /// How often the sweep that closes idle conversations runs. Conversations count against
    /// the per-bot cap of active anonymous sessions until they close, so the sweep has to
    /// run often, not once an hour.
    /// </summary>
    public int StaleSweepMinutes { get; set; } = 5;
}
