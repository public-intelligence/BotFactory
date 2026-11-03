using System.Text.Json;
using System.Text.Json.Nodes;

namespace BotFactory.Infrastructure.Tools.Execution;

/// <summary>
/// Write-time redaction for everything the tool pipeline persists or displays.
/// Values under sensitive-looking keys become "***"; nothing unredacted is ever
/// stored (execution rows, telemetry, test-console echoes).
/// </summary>
public static class ToolRedaction
{
    public const string Mask = "***";

    private static readonly string[] SensitiveKeyFragments =
    [
        "password", "secret", "token", "apikey", "api_key", "api-key",
        "authorization", "credential", "clave", "contrasena", "contraseña",
    ];

    public static bool IsSensitiveKey(string key)
    {
        foreach (var fragment in SensitiveKeyFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Parses, masks sensitive keys recursively, serializes, and truncates. Non-JSON input is truncated as text.</summary>
    public static string RedactJsonString(string? json, int maxLength)
    {
        if (string.IsNullOrEmpty(json))
            return "{}";

        try
        {
            var node = JsonNode.Parse(json);
            RedactNode(node);
            return Truncate(node?.ToJsonString() ?? "{}", maxLength);
        }
        catch (JsonException)
        {
            return Truncate(json, maxLength);
        }
    }

    public static string RedactElement(JsonElement element, int maxLength) =>
        RedactJsonString(element.GetRawText(), maxLength);

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static void RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitiveKey(key))
                        obj[key] = Mask;
                    else
                        RedactNode(obj[key]);
                }

                break;

            case JsonArray arr:
                foreach (var item in arr)
                    RedactNode(item);

                break;
        }
    }
}
