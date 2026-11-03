using System.Text.Json;
using BotFactory.Core.Entities;

namespace BotFactory.Api.Helpers;

public static class BotUpdateHelper
{
    /// <summary>
    /// Applies greeting only when <c>greetingMessage</c> is present in the JSON body.
    /// Omitted property leaves the stored value unchanged (partial updates from Voz tab).
    /// <c>null</c> or whitespace clears the greeting.
    /// </summary>
    public static void ApplyGreetingFromJson(JsonElement root, Bot bot)
    {
        if (!root.TryGetProperty("greetingMessage", out var greeting))
            return;

        bot.GreetingMessage = greeting.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => string.IsNullOrWhiteSpace(greeting.GetString())
                ? null
                : greeting.GetString()!.Trim(),
            _ => bot.GreetingMessage,
        };
    }
}
