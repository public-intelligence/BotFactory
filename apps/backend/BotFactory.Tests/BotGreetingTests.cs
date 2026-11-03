using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;

namespace BotFactory.Tests;

public class BotGreetingTests
{
    [Fact]
    public void ApplyUpdate_EmptyGreetingMessage_ClearsGreeting()
    {
        var bot = new Bot { GreetingMessage = "¡Hola! Estoy para servirte." };

        bot.ApplyUpdate(new UpdateBotRequest(GreetingMessage: ""));

        Assert.Null(bot.GreetingMessage);
    }

    [Fact]
    public void ApplyUpdate_NullGreetingMessage_LeavesGreetingUnchanged()
    {
        var bot = new Bot { GreetingMessage = "¡Hola! Estoy para servirte." };

        bot.ApplyUpdate(new UpdateBotRequest(GreetingMessage: null));

        Assert.Equal("¡Hola! Estoy para servirte.", bot.GreetingMessage);
    }
}
