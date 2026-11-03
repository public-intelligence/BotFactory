namespace BotFactory.KbScraper;

public class ScraperOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public int MaxConcurrentRenders { get; set; } = 4;
    public int DefaultTimeoutMs { get; set; } = 45_000;
    /// <summary>How long a request waits for a free browser slot before returning busy.</summary>
    public int AcquireTimeoutMs { get; set; } = 120_000;
    public int PostLoadWaitMs { get; set; } = 1500;
    public int MaxHtmlBytes { get; set; } = 5 * 1024 * 1024;
}
