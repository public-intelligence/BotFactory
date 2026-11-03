namespace BotFactory.Core.Entities;

/// <summary>
/// Canonical values for <see cref="Bot.PublishedState"/>.
/// Kept lowercase to match the existing DB seeds and default value.
/// </summary>
public static class BotStates
{
    public const string Draft = "draft";
    public const string Published = "published";

    public static bool IsValid(string? value) =>
        value is Draft or Published;
}
