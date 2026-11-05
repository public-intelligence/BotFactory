namespace BotFactory.Core.Entities;

/// <summary>
/// The words a visitor reads (or hears) when the assistant cannot answer for a technical reason,
/// not for lack of information: a limit hit when opening the chat, the model's quota spent, an
/// error or a timeout. Each bot can set its own (<see cref="Bot.RateLimitMessage"/>,
/// <see cref="Bot.QuotaMessage"/>, <see cref="Bot.ErrorMessage"/>); an empty one falls back to
/// these defaults, which address the visitor as «usted» like the rest of the bot's answers.
/// </summary>
public static class BotErrorMessages
{
    public const int MaxLength = 500;

    /// <summary>Opening the chat when the bot already has its cap of active anonymous conversations.</summary>
    public const string ConversationsLimitDefault =
        "El asistente está atendiendo demasiadas conversaciones. Intente de nuevo en unos minutos.";

    /// <summary>Opening the chat when a per-minute request limit is hit (bot, organization or address).</summary>
    public const string RequestsLimitDefault =
        "Demasiadas solicitudes. Intente de nuevo en unos minutos.";

    /// <summary>What is stored: trimmed, and null when nothing is left, so that «empty» always means «the default».</summary>
    public static string? Normalize(string? raw)
    {
        var text = raw?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>The reason a message cannot be saved, or null when it can.</summary>
    public static string? Validate(string? raw, string field)
    {
        var text = Normalize(raw);
        return text is not null && text.Length > MaxLength
            ? $"{field} is longer than {MaxLength} characters ({text.Length})."
            : null;
    }

    /// <summary>The bot's own message when it set one, otherwise <paramref name="fallback"/>.</summary>
    public static string Resolve(string? configured, string fallback) =>
        Normalize(configured) ?? fallback;
}
