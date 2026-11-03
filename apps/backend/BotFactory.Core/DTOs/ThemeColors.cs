using System.Text.RegularExpressions;

namespace BotFactory.Core.DTOs;

/// <summary>
/// Validates and normalizes per-bot theme hex colors (#RGB or #RRGGBB → #RRGGBB).
/// Empty / whitespace clears to null (platform default).
/// </summary>
public static partial class ThemeColors
{
    [GeneratedRegex(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    /// <summary>
    /// Returns null when <paramref name="value"/> is null/empty (clear),
    /// the normalized #RRGGBB when valid, or throws <see cref="FormatException"/> when invalid.
    /// Callers that need a soft failure should use <see cref="TryNormalize"/>.
    /// </summary>
    public static string? NormalizeOrNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!TryNormalize(value, out var normalized))
            throw new FormatException($"Invalid theme color '{value}'. Use #RGB or #RRGGBB.");

        return normalized;
    }

    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return true; // clear → null is valid

        var trimmed = value.Trim();
        if (!HexPattern().IsMatch(trimmed))
            return false;

        var hex = trimmed[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        normalized = "#" + hex.ToUpperInvariant();
        return true;
    }

    /// <summary>
    /// Returns an error message if any provided theme color is invalid; otherwise null.
    /// Empty strings are treated as clear (valid).
    /// </summary>
    public static string? ValidateRequest(string? background, string? primary, string? accent)
    {
        foreach (var (name, value) in new[]
        {
            ("themeBackground", background),
            ("themePrimary", primary),
            ("themeAccent", accent),
        })
        {
            if (value is null) continue;
            if (!TryNormalize(value, out _))
                return $"Invalid {name} '{value}'. Use #RGB or #RRGGBB.";
        }

        return null;
    }
}
