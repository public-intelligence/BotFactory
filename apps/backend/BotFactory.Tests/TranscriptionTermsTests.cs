using BotFactory.Core.Entities;

namespace BotFactory.Tests;

public class TranscriptionTermsTests
{
    [Fact]
    public void Parse_AcceptsOneTermPerLine_AndCommasAndSemicolons()
    {
        var terms = TranscriptionTerms.Parse("Aurora\nABCD, XYZ; DEFG\r\nPunto Ciudadano");

        Assert.Equal(["Aurora", "ABCD", "XYZ", "DEFG", "Punto Ciudadano"], terms);
    }

    [Fact]
    public void Parse_TrimsCollapsesSpacesAndDropsEmptiesAndRepeats()
    {
        var terms = TranscriptionTerms.Parse("  Punto   Ciudadano \n\n punto ciudadano\n  \nAvanza\nAVANZA");

        // The first spelling is kept; case does not make a new term.
        Assert.Equal(["Punto Ciudadano", "Avanza"], terms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n , ; ")]
    public void Parse_OfNothing_IsEmpty(string? raw)
    {
        Assert.Empty(TranscriptionTerms.Parse(raw));
        Assert.Equal(string.Empty, TranscriptionTerms.Normalize(raw));
        Assert.Null(TranscriptionTerms.Validate(raw));
    }

    [Fact]
    public void Normalize_StoresOneTermPerLine()
    {
        Assert.Equal("Aurora\nXYZ\nPunto Ciudadano", TranscriptionTerms.Normalize("Aurora, XYZ\r\nPunto  Ciudadano"));
    }

    [Fact]
    public void Validate_AcceptsTypicalTerms()
    {
        const string raw = "Aurora\nABCD\nXYZ\nDEFG\nAvanza\nPunto Ciudadano\nQuisqueya\nZETA";

        Assert.Null(TranscriptionTerms.Validate(raw));
        Assert.Equal(8, TranscriptionTerms.Parse(raw).Count);
    }

    [Fact]
    public void Validate_AcceptsExactlyTheLimits()
    {
        var raw = string.Join('\n', Enumerable.Range(0, TranscriptionTerms.MaxTerms).Select(i => $"t{i:00}" + new string('x', TranscriptionTerms.MaxTermLength - 3)));

        Assert.Null(TranscriptionTerms.Validate(raw));
    }

    [Fact]
    public void Validate_RejectsMoreTermsThanTheRecogniserTakes()
    {
        var raw = string.Join('\n', Enumerable.Range(0, TranscriptionTerms.MaxTerms + 1).Select(i => $"término{i}"));

        var error = TranscriptionTerms.Validate(raw);

        Assert.NotNull(error);
        Assert.Contains("Too many", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsATermLongerThanTheRecogniserTakes_NamingIt()
    {
        var error = TranscriptionTerms.Validate("Aurora\nInstituto Nacional de Tránsito y Transporte Terrestre\nXYZ");

        Assert.NotNull(error);
        Assert.Contains("Instituto Nacional de Tránsito y Transporte Terrestre", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurora", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CountsTermsAfterRepeatsAreDropped()
    {
        // 60 lines but only 3 different terms: nothing to reject.
        var raw = string.Join('\n', Enumerable.Range(0, 60).Select(i => (i % 3) switch { 0 => "XYZ", 1 => "xyz", _ => "DEFG" }));

        Assert.Null(TranscriptionTerms.Validate(raw));
    }
}
