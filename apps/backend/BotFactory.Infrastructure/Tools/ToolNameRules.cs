using System.Text.RegularExpressions;
using BotFactory.Core.Tools;

namespace BotFactory.Infrastructure.Tools;

/// <summary>
/// Validation for configured tool names. Built-in handler names are reserved
/// (taken live from the DI handler list, so future system tools are covered
/// automatically) — a configured tool can never shadow kb_search.
/// </summary>
public static partial class ToolNameRules
{
    [GeneratedRegex("^[a-z][a-z0-9_]{2,63}$")]
    private static partial Regex NameRegex();

    public static bool IsValidFormat(string name) => NameRegex().IsMatch(name);

    /// <summary>Returns a Spanish admin-facing error, or null when the name is acceptable.</summary>
    public static string? Validate(string name, IEnumerable<IToolHandler> builtInHandlers)
    {
        // Reserved check first (case-insensitive) so any casing of a built-in
        // name gets the accurate "reservado" message, not a format complaint.
        foreach (var handler in builtInHandlers)
        {
            if (string.Equals(handler.Name, name, StringComparison.OrdinalIgnoreCase))
                return $"El nombre '{name}' está reservado para una herramienta del sistema.";
        }

        if (!IsValidFormat(name))
        {
            return "El nombre técnico debe estar en snake_case: solo minúsculas, números y guiones bajos, "
                + "empezando con una letra (3–64 caracteres). Ejemplo: consultar_expediente.";
        }

        return null;
    }
}
