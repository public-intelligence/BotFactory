using BotFactory.Infrastructure.Services;

namespace BotFactory.Tests;

public class KbUrlClassifierTests
{
    [Theory]
    [InlineData("https://www.mtss.go.cr/temas-laborales/salarios/LISTA%20DE%20SALARIOS%20MINIMOS%20%20ANO%202023.pdf", true)]
    [InlineData("https://example.go.cr/document.PDF", true)]
    [InlineData("https://example.go.cr/page.html", false)]
    [InlineData("https://example.go.cr/beneficio", false)]
    public void IsPdfUrl_DetectsPdfPaths(string url, bool expected)
    {
        var uri = new Uri(url);
        Assert.Equal(expected, KbUrlClassifier.IsPdfUrl(uri));
    }
}
