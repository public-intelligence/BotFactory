using BotFactory.KbScraper.Contracts;

namespace BotFactory.KbScraper;

public interface IRenderService
{
    Task<(bool Success, RenderResponse? Response, RenderErrorResponse? Error)> RenderAsync(
        RenderRequest request,
        CancellationToken cancellationToken = default);
}
