using BotFactory.Core.Services;
using Xunit;

namespace BotFactory.Tests;

public class WrittenToolCallTests
{
    private static readonly string[] Tools = ["lugares", "kb_search", "fecha_actual"];

    [Fact]
    public void Parse_Conversation2396_IsTheCallTheModelMeant()
    {
        var parsed = WrittenToolCall.Parse("default_api.lugares(que='salud', lugar='Santiago', tipo='hospital')\n", Tools);

        Assert.Equal(WrittenToolCall.Kind.Calls, parsed.Kind);
        var call = Assert.Single(parsed.Calls);
        Assert.Equal("lugares", call.Name);
        Assert.Equal("salud", call.Args.GetProperty("que").GetString());
        Assert.Equal("Santiago", call.Args.GetProperty("lugar").GetString());
        Assert.Equal("hospital", call.Args.GetProperty("tipo").GetString());
    }

    [Fact]
    public void Parse_Conversation1994_WithDoubleQuotesAndAClosingFence_IsRead()
    {
        var parsed = WrittenToolCall.Parse("default_api.lugares(que=\"salud\", lugar=\"Santiago\", tipo=\"hospital\")\n```", Tools);

        var call = Assert.Single(parsed.Calls);
        Assert.Equal("lugares", call.Name);
        Assert.Equal("hospital", call.Args.GetProperty("tipo").GetString());
    }

    [Theory]
    [InlineData("```tool_code\nprint(default_api.lugares(que='salud', lugar='Santiago'))\n```")]
    [InlineData("print(default_api.lugares(que='salud', lugar='Santiago'))")]
    [InlineData("tool_code\ndefault_api.lugares(que='salud', lugar='Santiago')")]
    [InlineData("lugares(que='salud', lugar='Santiago')")]
    public void Parse_TheShapesGeminiUses_AreRead(string text)
    {
        var call = Assert.Single(WrittenToolCall.Parse(text, Tools).Calls);

        Assert.Equal("lugares", call.Name);
        Assert.Equal("Santiago", call.Args.GetProperty("lugar").GetString());
    }

    [Fact]
    public void Parse_NumbersBooleansNoneAndEscapes_BecomeJson()
    {
        var call = Assert.Single(WrittenToolCall.Parse(
            "default_api.kb_search(query='la \\'ficha\\' del pasaporte', limite=3, peso=0.5, exacto=True, filtro=None)",
            Tools).Calls);

        Assert.Equal("la 'ficha' del pasaporte", call.Args.GetProperty("query").GetString());
        Assert.Equal(3, call.Args.GetProperty("limite").GetInt32());
        Assert.Equal(0.5, call.Args.GetProperty("peso").GetDouble());
        Assert.True(call.Args.GetProperty("exacto").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, call.Args.GetProperty("filtro").ValueKind);
    }

    [Fact]
    public void Parse_TwoCallsOnTwoLines_AreBothRead()
    {
        var parsed = WrittenToolCall.Parse("default_api.fecha_actual()\ndefault_api.lugares(lugar='Santiago')", Tools);

        Assert.Equal(["fecha_actual", "lugares"], parsed.Calls.Select(c => c.Name));
        Assert.Empty(parsed.Calls[0].Args.EnumerateObject());
    }

    [Fact]
    public void Parse_AToolTheBotDoesNotHave_IsStillACall_WithThePrefix()
    {
        // The turn then answers it like any unknown tool (herramienta_inexistente).
        var call = Assert.Single(WrittenToolCall.Parse("default_api.places(lugar='Santiago')", Tools).Calls);

        Assert.Equal("places", call.Name);
    }

    [Theory]
    [InlineData("default_api.lugares('salud', 'Santiago')")] // positional
    [InlineData("default_api.lugares(lugar=['Santiago'])")] // a list
    [InlineData("default_api.lugares(lugar='Santiago'")] // cut
    public void Parse_ACallThatCannotBeRead_IsUnreadable_NotText(string text)
    {
        Assert.Equal(WrittenToolCall.Kind.Unreadable, WrittenToolCall.Parse(text, Tools).Kind);
    }

    [Theory]
    [InlineData("Men senk lopital piblik nan Santiago:\n\n* Presidente Estrella Urena")]
    [InlineData("lugares públicos de Santiago: el hospital José María Cabral y Báez.")]
    [InlineData("de acuerdo con su fuente, el trámite es gratis.")]
    [InlineData("```cobol\nIDENTIFICATION DIVISION.\n```")]
    [InlineData("calcular(x=1)")] // not one of the bot's tools, and no prefix
    public void Parse_OrdinaryText_IsNotACall(string text)
    {
        Assert.Equal(WrittenToolCall.Kind.NotACall, WrittenToolCall.Parse(text, Tools).Kind);
    }

    [Theory]
    [InlineData("d", WrittenToolCall.HeadDecision.Undecided)]
    [InlineData("default_a", WrittenToolCall.HeadDecision.Undecided)]
    [InlineData("default_api.lug", WrittenToolCall.HeadDecision.Hold)]
    [InlineData("  \ndefault_api.", WrittenToolCall.HeadDecision.Hold)]
    [InlineData("print(default_api.", WrittenToolCall.HeadDecision.Hold)]
    [InlineData("```tool_code", WrittenToolCall.HeadDecision.Hold)]
    [InlineData("lugares(", WrittenToolCall.HeadDecision.Hold)]
    [InlineData("lugares", WrittenToolCall.HeadDecision.Undecided)]
    [InlineData("lugares ", WrittenToolCall.HeadDecision.Release)]
    [InlineData("```c", WrittenToolCall.HeadDecision.Release)]
    [InlineData("de ", WrittenToolCall.HeadDecision.Release)]
    [InlineData("Men senk", WrittenToolCall.HeadDecision.Release)]
    [InlineData("Para renovar", WrittenToolCall.HeadDecision.Release)]
    [InlineData("", WrittenToolCall.HeadDecision.Undecided)]
    public void ClassifyHead_HoldsOnlyWhatCouldStillBeAWrittenCall(string head, WrittenToolCall.HeadDecision expected)
    {
        Assert.Equal(expected, WrittenToolCall.ClassifyHead(head, Tools));
    }
}
