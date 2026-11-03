using BotFactory.Core.DTOs;
using BotFactory.Infrastructure.Services;

namespace BotFactory.Api.Endpoints;

public static class VoiceEndpoints
{
    public static void MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/voices")
            .RequireAuthorization()
            .WithTags("Voices");

        group.MapGet("/elevenlabs", async (
            ElevenLabsVoicesService service,
            CancellationToken ct,
            bool refresh = false) =>
        {
            var result = await service.GetVoicesAsync(refresh, ct);
            return Results.Ok(result);
        })
        .WithName("GetElevenLabsVoices")
        .Produces<ElevenLabsVoicesResponse>();
    }
}
