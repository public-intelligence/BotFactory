using System.Text.Json;
using BotFactory.Infrastructure.Tools.Execution;

namespace BotFactory.Tests;

public class ToolArgumentValidatorTests
{
    private const string Schema =
        """
        {
          "type": "object",
          "properties": {
            "numero": { "type": "string", "description": "Número de expediente" },
            "anio": { "type": "integer" }
          },
          "required": ["numero"]
        }
        """;

    private static JsonElement Args(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ValidArguments_Pass()
    {
        var validator = new ToolArgumentValidator();
        Assert.Empty(validator.Validate(Schema, Args("""{"numero":"EXP-1","anio":2026}""")));
    }

    [Fact]
    public void MissingRequiredField_Fails()
    {
        var validator = new ToolArgumentValidator();
        var problems = validator.Validate(Schema, Args("""{"anio":2026}"""));
        Assert.NotEmpty(problems);
    }

    [Fact]
    public void WrongType_Fails()
    {
        var validator = new ToolArgumentValidator();
        var problems = validator.Validate(Schema, Args("""{"numero":"EXP-1","anio":"no-numero"}"""));
        Assert.NotEmpty(problems);
    }

    [Fact]
    public void InvalidSchemaDocument_FailsClosed()
    {
        var validator = new ToolArgumentValidator();
        var problems = validator.Validate("not json at all {", Args("{}"));
        Assert.NotEmpty(problems);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{}}""")]
    [InlineData("""{"type":"object","properties":{"q":{"type":"string"}},"required":["q"]}""")]
    public void ValidateSchemaDocument_AcceptsObjectSchemas(string schema)
    {
        Assert.Null(ToolArgumentValidator.ValidateSchemaDocument(schema));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"string\"")]
    [InlineData("""{"type":"array"}""")]
    [InlineData("not json")]
    public void ValidateSchemaDocument_RejectsNonObjectRoots(string schema)
    {
        Assert.NotNull(ToolArgumentValidator.ValidateSchemaDocument(schema));
    }
}
