using BotFactory.Core.DTOs;

namespace BotFactory.Tests;

public class ThemeColorsTests
{
    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#AABBCC", "#AABBCC")]
    [InlineData("#aAbBcC", "#AABBCC")]
    [InlineData("  #fff  ", "#FFFFFF")]
    public void TryNormalize_ValidHex_Normalizes(string input, string expected)
    {
        Assert.True(ThemeColors.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_Empty_ClearsToNull(string? input)
    {
        Assert.True(ThemeColors.TryNormalize(input, out var normalized));
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#gg0000")]
    [InlineData("#12")]
    [InlineData("AABBCC")]
    public void TryNormalize_Invalid_Fails(string input)
    {
        Assert.False(ThemeColors.TryNormalize(input, out _));
    }

    [Fact]
    public void ValidateRequest_RejectsInvalidAccent()
    {
        var error = ThemeColors.ValidateRequest("#000000", null, "nope");
        Assert.Contains("themeAccent", error);
    }

    [Fact]
    public void NormalizeOrNull_Empty_ReturnsNull()
    {
        Assert.Null(ThemeColors.NormalizeOrNull(""));
    }
}
