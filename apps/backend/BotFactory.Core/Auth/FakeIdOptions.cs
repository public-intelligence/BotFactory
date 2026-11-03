namespace BotFactory.Core.Auth;

public class FakeIdOptions
{
    public string Issuer { get; set; } = "bot-factory-dev";
    public string Audience { get; set; } = "bot-factory";
    public string SigningKey { get; set; } = "dev-signing-key-at-least-32-chars-long!!";
}
