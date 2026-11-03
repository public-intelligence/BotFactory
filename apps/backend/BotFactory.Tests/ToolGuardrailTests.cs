using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Tools;

namespace BotFactory.Tests;

/// <summary>
/// Built-in tool guardrails (plan §4.5): kb_search is always present, cannot
/// be shadowed by configuration, and a config-free bot's payload is identical
/// to the pre-feature composition.
/// </summary>
public class ToolGuardrailTests
{
    private sealed class StubBuiltInHandler : IToolHandler
    {
        public string Name => "kb_search";

        public FunctionDeclaration Declaration { get; } = new()
        {
            Name = "kb_search",
            Description = "Busca en la base de conocimiento.",
            Parameters = new JsonSchema
            {
                Type = "object",
                Properties = new() { ["query"] = new JsonSchema { Type = "string" } },
                Required = ["query"],
            },
        };

        public Task<ToolInvocation> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken ct) =>
            Task.FromResult(new ToolInvocation(new { ok = true }, null, false));
    }

    private sealed class StubToolsetContext(IReadOnlyList<ResolvedBotTool> tools) : IToolsetContext
    {
        public Bot? Bot => null;
        public IReadOnlyList<ResolvedBotTool> EnabledTools => tools;
    }

    private sealed class ThrowingGateway : IToolExecutionGateway
    {
        public Task<ToolInvocation> ExecuteAsync(ResolvedBotTool tool, JsonElement args, ToolContext context, CancellationToken ct) =>
            throw new InvalidOperationException("gateway must not run in these tests");

        public Task<ToolTestResult> ExecuteTestAsync(ToolDefinition definition, ToolConnection connection, JsonElement args, CancellationToken ct) =>
            throw new InvalidOperationException("gateway must not run in these tests");
    }

    private static ResolvedBotTool MakeConfiguredTool(string name) => new(
        new ToolDefinition
        {
            Name = name,
            DisplayName = name,
            AiDescription = "desc",
            ParametersSchemaJson = """{"type":"object","properties":{}}""",
            Status = ToolDefinitionStatus.Published,
        },
        new ToolConnection { Name = "conn", BaseUrl = "https://example.test" },
        new BotTool { Enabled = true });

    // ── Reserved names ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("kb_search")]
    [InlineData("KB_SEARCH")]
    [InlineData("Kb_Search")]
    public void ToolNameRules_RejectsBuiltInNames_AnyCasing(string name)
    {
        var error = ToolNameRules.Validate(name.ToLowerInvariant() == name ? name : name, [new StubBuiltInHandler()]);
        Assert.NotNull(error);
        Assert.Contains("reservado", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("consultar_expediente")]
    [InlineData("clima_actual")]
    [InlineData("a2_b3")]
    public void ToolNameRules_AcceptsValidNames(string name)
    {
        Assert.Null(ToolNameRules.Validate(name, [new StubBuiltInHandler()]));
    }

    [Theory]
    [InlineData("Consultar")]
    [InlineData("con-sultar")]
    [InlineData("ab")]
    [InlineData("1tool")]
    [InlineData("")]
    public void ToolNameRules_RejectsInvalidFormats(string name)
    {
        Assert.NotNull(ToolNameRules.Validate(name, []));
    }

    // ── Registry composition ───────────────────────────────────────────────

    [Fact]
    public void Registry_WithEmptyToolset_IsIdenticalToBuiltInsOnly()
    {
        var builtIn = new StubBuiltInHandler();
        var registry = new ToolRegistry(
            [builtIn], new StubToolsetContext([]), new ThrowingGateway());

        var payload = registry.BuildGeminiToolsPayload();

        Assert.Single(payload);
        var declaration = Assert.Single(payload[0].FunctionDeclarations);
        Assert.Same(builtIn.Declaration, declaration);
        Assert.Same(builtIn, registry.Get("kb_search"));
    }

    [Fact]
    public void Registry_ConfiguredToolCannotShadowBuiltIn()
    {
        var builtIn = new StubBuiltInHandler();
        var registry = new ToolRegistry(
            [builtIn],
            new StubToolsetContext([MakeConfiguredTool("kb_search")]),
            new ThrowingGateway());

        // The built-in wins the collision; only one declaration exists.
        var payload = registry.BuildGeminiToolsPayload();
        var declaration = Assert.Single(payload[0].FunctionDeclarations);
        Assert.Same(builtIn.Declaration, declaration);
        Assert.Same(builtIn, registry.Get("kb_search"));
    }

    [Fact]
    public void Registry_ComposesBuiltInsFirstThenConfiguredTools()
    {
        var builtIn = new StubBuiltInHandler();
        var registry = new ToolRegistry(
            [builtIn],
            new StubToolsetContext([MakeConfiguredTool("consultar_estado")]),
            new ThrowingGateway());

        var payload = registry.BuildGeminiToolsPayload();
        var names = payload[0].FunctionDeclarations.Select(d => d.Name).ToList();

        Assert.Equal(["kb_search", "consultar_estado"], names);
        Assert.IsType<ConfiguredToolHandler>(registry.Get("consultar_estado"));
        Assert.Same(builtIn, registry.Get("kb_search"));
    }

    // ── System-instruction suffix ──────────────────────────────────────────

    [Fact]
    public void ToolInstructionPrompt_NoTools_LeavesInstructionUnchanged()
    {
        Assert.Equal("BASE", ToolInstructionPrompt.Append("BASE", []));
    }

    [Fact]
    public void ToolInstructionPrompt_AppendsSecurityPreambleAndNotes()
    {
        var tool = MakeConfiguredTool("consultar_estado");
        tool.Definition.AiUsageNotes = "Úsala solo con número de expediente.";
        tool.Association.AdditionalInstructions = "En este bot, pide siempre la cédula.";

        var result = ToolInstructionPrompt.Append("BASE", [tool]);

        Assert.StartsWith("BASE", result, StringComparison.Ordinal);
        Assert.Contains("Herramientas externas", result, StringComparison.Ordinal);
        Assert.Contains("nunca inventes un resultado", result, StringComparison.Ordinal);
        Assert.Contains("### consultar_estado", result, StringComparison.Ordinal);
        Assert.Contains("número de expediente", result, StringComparison.Ordinal);
        Assert.Contains("pide siempre la cédula", result, StringComparison.Ordinal);
    }
}
