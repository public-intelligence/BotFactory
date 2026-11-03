namespace BotFactory.Core.Services;

public class KbScraperOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 45;
    public bool Enabled { get; set; } = true;
}
