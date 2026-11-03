namespace BotFactory.Core.Entities;

/// <summary>
/// Canonical values for <see cref="Conversation.AccessMode"/> — how the end
/// user was identified when the conversation started.
/// </summary>
public static class ConversationAccessModes
{
    public const string Authenticated = "authenticated";
    public const string Public = "public";
}

/// <summary>
/// Canonical values for <see cref="Conversation.Surface"/> — where the chat UI
/// was running (direct link vs. embedded iframe).
/// </summary>
public static class ConversationSurfaces
{
    public const string Direct = "direct";
    public const string Iframe = "iframe";
}
