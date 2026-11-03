using BotFactory.Core.Tools;

namespace BotFactory.Infrastructure.Tools;

/// <summary>
/// Bot-aware tool registry (scoped). Composes lazily on first use: built-in
/// handlers from DI first — always and unconditionally (kb_search guardrail,
/// planning/external-tools-plan.md §4.5) — then ConfiguredToolHandler instances
/// for the toolset context's bot. When no bot was set on the context, the
/// composition is built-ins only: byte-identical to the pre-feature payload.
/// Name collisions are prevented at save time (reserved names); the merge is
/// defensive anyway — a configured tool can never shadow a built-in.
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly IEnumerable<IToolHandler> _builtInHandlers;
    private readonly IToolsetContext _toolset;
    private readonly IToolExecutionGateway _gateway;

    private Dictionary<string, IToolHandler>? _byName;
    private GeminiToolGroup[]? _payload;

    public ToolRegistry(
        IEnumerable<IToolHandler> builtInHandlers,
        IToolsetContext toolset,
        IToolExecutionGateway gateway)
    {
        _builtInHandlers = builtInHandlers;
        _toolset = toolset;
        _gateway = gateway;
    }

    public GeminiToolGroup[] BuildGeminiToolsPayload()
    {
        EnsureComposed();
        return _payload!;
    }

    public IToolHandler? Get(string name)
    {
        EnsureComposed();
        return _byName!.GetValueOrDefault(name);
    }

    private void EnsureComposed()
    {
        if (_byName is not null)
            return;

        var byName = new Dictionary<string, IToolHandler>(StringComparer.Ordinal);

        foreach (var handler in _builtInHandlers)
            byName[handler.Name] = handler;

        foreach (var tool in _toolset.EnabledTools)
        {
            if (byName.ContainsKey(tool.Definition.Name))
                continue; // built-ins win

            byName[tool.Definition.Name] = new ConfiguredToolHandler(tool, _gateway);
        }

        _byName = byName;
        _payload = byName.Count == 0
            ? []
            :
            [
                new GeminiToolGroup
                {
                    FunctionDeclarations = byName.Values.Select(h => h.Declaration).ToArray(),
                },
            ];
    }
}
