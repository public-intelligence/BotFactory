using BotFactory.Infrastructure.Services;

namespace BotFactory.Tests;

public class KbUrlValidatorTests
{
    [Theory]
    [InlineData("https://www.example.go.cr/path", true)]
    [InlineData("http://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("not-a-url", false)]
    [InlineData("https://localhost/secret", false)]
    [InlineData("http://127.0.0.1/x", false)]
    [InlineData("http://192.168.1.10/x", false)]
    [InlineData("http://10.0.0.5/x", false)]
    public void TryNormalize_ValidatesSchemesAndHosts(string raw, bool expectedOk)
    {
        var ok = KbUrlValidator.TryNormalize(raw, out var normalized, out var error);
        Assert.Equal(expectedOk, ok);
        if (expectedOk)
        {
            Assert.False(string.IsNullOrWhiteSpace(normalized));
            Assert.Null(error);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void TryNormalize_DedupesFragmentAndTrims()
    {
        var ok = KbUrlValidator.TryNormalize("  https://example.com/a#section  ", out var normalized, out _);
        Assert.True(ok);
        Assert.Equal("https://example.com/a", normalized);
    }
}
