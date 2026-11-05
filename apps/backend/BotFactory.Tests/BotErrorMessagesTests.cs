using BotFactory.Core.Entities;

namespace BotFactory.Tests;

public class BotErrorMessagesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   \n ", null)]
    [InlineData("  Estimado ciudadano.  ", "Estimado ciudadano.")]
    public void Normalize_TrimsAndTurnsNothingIntoNull(string? raw, string? expected)
    {
        Assert.Equal(expected, BotErrorMessages.Normalize(raw));
    }

    [Fact]
    public void Validate_AcceptsUpToTheLimit_AndNamesTheFieldOverIt()
    {
        Assert.Null(BotErrorMessages.Validate(null, "Quota message"));
        Assert.Null(BotErrorMessages.Validate(new string('x', BotErrorMessages.MaxLength), "Quota message"));

        var error = BotErrorMessages.Validate(new string('x', BotErrorMessages.MaxLength + 1), "Quota message");

        Assert.NotNull(error);
        Assert.Contains("Quota message", error, StringComparison.Ordinal);
        Assert.Contains(BotErrorMessages.MaxLength.ToString(), error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CountsWhatIsStored_NotTheSpacesAroundIt()
    {
        var padded = "  " + new string('x', BotErrorMessages.MaxLength) + "  ";

        Assert.Null(BotErrorMessages.Validate(padded, "Error message"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithoutAMessage_IsTheDefault(string? configured)
    {
        Assert.Equal("por omisión", BotErrorMessages.Resolve(configured, "por omisión"));
    }

    [Fact]
    public void Resolve_WithAMessage_IsTheBotsOwn_Trimmed()
    {
        Assert.Equal("Su texto.", BotErrorMessages.Resolve("  Su texto. ", "por omisión"));
    }

    [Fact]
    public void TheDefaultsAddressTheVisitorAsUsted()
    {
        // «Intenta» (tú) would sit oddly next to a bot that says «puede», «vea», «usted».
        Assert.Contains("Intente", BotErrorMessages.ConversationsLimitDefault, StringComparison.Ordinal);
        Assert.Contains("Intente", BotErrorMessages.RequestsLimitDefault, StringComparison.Ordinal);
        Assert.DoesNotContain("Intenta", BotErrorMessages.ConversationsLimitDefault, StringComparison.Ordinal);
        Assert.DoesNotContain("Intenta", BotErrorMessages.RequestsLimitDefault, StringComparison.Ordinal);
    }
}
