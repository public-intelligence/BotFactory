using System.Text.Json;
using BotFactory.Api.Helpers;
using BotFactory.Core.Entities;

namespace BotFactory.Tests;

public class BotUpdateHelperTests
{
    [Fact]
    public void ApplyGreetingFromJson_OmittedProperty_LeavesGreetingUnchanged()
    {
        var bot = new Bot { GreetingMessage = "Hola" };
        using var doc = JsonDocument.Parse("""{"botName":"x"}""");

        BotUpdateHelper.ApplyGreetingFromJson(doc.RootElement, bot);

        Assert.Equal("Hola", bot.GreetingMessage);
    }

    [Fact]
    public void ApplyGreetingFromJson_NullOrEmpty_ClearsGreeting()
    {
        var bot = new Bot { GreetingMessage = "Hola" };

        using (var nullDoc = JsonDocument.Parse("""{"greetingMessage":null}"""))
        {
            BotUpdateHelper.ApplyGreetingFromJson(nullDoc.RootElement, bot);
            Assert.Null(bot.GreetingMessage);
        }

        bot.GreetingMessage = "Hola";
        using var emptyDoc = JsonDocument.Parse("""{"greetingMessage":""}""");
        BotUpdateHelper.ApplyGreetingFromJson(emptyDoc.RootElement, bot);
        Assert.Null(bot.GreetingMessage);
    }
}
