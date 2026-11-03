using System.Globalization;
using System.Text;

namespace BotFactory.Infrastructure.Tools.Execution;

/// <summary>
/// Strips diacritics from strings via Unicode decomposition (FormD), dropping
/// combining marks and recomposing (FormC): á→a, ó→o, ñ→n, Ñ→N, ü→u. Used for
/// outbound tool arguments when ToolDefinition.NormalizeArgDiacritics is on.
/// </summary>
public static class DiacriticsNormalizer
{
    public static string Strip(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder? sb = null;
        for (var i = 0; i < decomposed.Length; i++)
        {
            var c = decomposed[i];
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                sb ??= new StringBuilder(decomposed[..i], decomposed.Length);
                continue;
            }

            sb?.Append(c);
        }

        return sb is null ? value : sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
