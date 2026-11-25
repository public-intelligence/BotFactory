namespace BotFactory.Core.Auth;

public class FakeIdOptions
{
    public string Issuer { get; set; } = "bot-factory-dev";
    public string Audience { get; set; } = "bot-factory";
    // No built-in key: a default written in the source would be a known secret. Development gets one from
    // appsettings.Development.json; any other environment must configure its own (>= 32 characters).
    public string SigningKey { get; set; } = string.Empty;
}
