namespace BotFactory.KbScraper.Contracts;

public record RenderResponse(string Html, string FinalUrl, int StatusCode, long RenderMs);
