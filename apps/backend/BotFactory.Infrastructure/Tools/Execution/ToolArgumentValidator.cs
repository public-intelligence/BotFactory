using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace BotFactory.Infrastructure.Tools.Execution;

/// <summary>
/// Validates model-proposed tool arguments against the tool's declared JSON
/// Schema (JsonSchema.Net, draft 2020-12). The model never reaches a connector
/// with arguments the schema does not allow.
/// </summary>
public class ToolArgumentValidator
{
    /// <summary>Returns human-readable problems; empty when the arguments are valid.</summary>
    public IReadOnlyList<string> Validate(string parametersSchemaJson, JsonElement args)
    {
        Json.Schema.JsonSchema schema;
        try
        {
            schema = Json.Schema.JsonSchema.FromText(parametersSchemaJson);
        }
        catch (JsonException)
        {
            // Save-time validation makes this unreachable in practice; fail
            // closed rather than letting unvalidated args through.
            return ["La definición de parámetros de la herramienta no es un JSON Schema válido."];
        }

        var results = schema.Evaluate(args, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (results.IsValid)
            return [];

        var problems = new List<string>();
        foreach (var detail in results.Details ?? [])
        {
            if (detail.IsValid || detail.Errors is null || detail.Errors.Count == 0)
                continue;

            var location = detail.InstanceLocation.ToString();
            var where = string.IsNullOrEmpty(location) || location == "#" ? "(raíz)" : location;
            foreach (var error in detail.Errors.Values)
                problems.Add($"{where}: {error}");
        }

        if (problems.Count == 0)
            problems.Add("Los argumentos no cumplen el esquema declarado.");

        return problems;
    }

    /// <summary>
    /// Save-time check that a ParametersSchemaJson document is itself a valid
    /// schema with an object root. Returns a Spanish admin-facing error or null.
    /// </summary>
    public static string? ValidateSchemaDocument(string parametersSchemaJson)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(parametersSchemaJson);
        }
        catch (JsonException)
        {
            return "El esquema de parámetros no es JSON válido.";
        }

        if (node is not JsonObject obj)
            return "El esquema de parámetros debe ser un objeto JSON Schema.";

        var type = obj["type"]?.GetValue<string>();
        if (!string.Equals(type, "object", StringComparison.Ordinal))
            return "El esquema de parámetros debe tener \"type\": \"object\" en la raíz.";

        try
        {
            _ = Json.Schema.JsonSchema.FromText(parametersSchemaJson);
        }
        catch (Exception)
        {
            return "El esquema de parámetros no es un JSON Schema válido.";
        }

        return null;
    }
}
