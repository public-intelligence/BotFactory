using System.Text.Json;
using BotFactory.Core.Tools;

namespace BotFactory.Infrastructure.Tools;

/// <summary>
/// IToolHandler façade over one configured (DB-defined) tool. The chat-turn
/// tool loop calls ExecuteAsync exactly as it does for built-ins; everything
/// specific to external execution lives behind the gateway.
/// </summary>
public sealed class ConfiguredToolHandler : IToolHandler
{
    // Parses ParametersSchemaJson (camelCase keys) into the Core JsonSchema
    // record; unknown keywords are dropped, which also strips anything Gemini
    // would reject.
    private static readonly JsonSerializerOptions SchemaOptions = new(JsonSerializerDefaults.Web);

    private readonly ResolvedBotTool _tool;
    private readonly IToolExecutionGateway _gateway;
    private FunctionDeclaration? _declaration;

    public ConfiguredToolHandler(ResolvedBotTool tool, IToolExecutionGateway gateway)
    {
        _tool = tool;
        _gateway = gateway;
    }

    public string Name => _tool.Definition.Name;

    public FunctionDeclaration Declaration => _declaration ??= BuildDeclaration();

    public Task<ToolInvocation> ExecuteAsync(
        JsonElement args,
        ToolContext context,
        CancellationToken cancellationToken)
        => _gateway.ExecuteAsync(_tool, args, context, cancellationToken);

    private FunctionDeclaration BuildDeclaration()
    {
        JsonSchema parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<JsonSchema>(
                    _tool.Definition.ParametersSchemaJson, SchemaOptions)
                ?? EmptyObjectSchema();
        }
        catch (JsonException)
        {
            // Save-time validation should make this unreachable; degrade to a
            // parameterless declaration rather than break the whole toolset.
            parameters = EmptyObjectSchema();
        }

        return new FunctionDeclaration
        {
            Name = _tool.Definition.Name,
            Description = _tool.Definition.AiDescription,
            Parameters = parameters,
        };
    }

    private static JsonSchema EmptyObjectSchema() => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchema>(),
    };
}
