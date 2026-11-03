namespace BotFactory.KbScraper.Contracts;

public record RenderRequest(string Url, int? TimeoutMs = null);
