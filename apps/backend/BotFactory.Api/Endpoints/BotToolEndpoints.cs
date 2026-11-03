using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Api.Endpoints;

public static class BotToolEndpoints
{
    public static void MapBotToolEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bots/{botId:int}/tools")
            .RequireAuthorization()
            .WithTags("Bot Tools");

        // Every published tool of the org with its association state for this
        // bot. Built-ins (kb_search) are not listed: always active, not
        // configurable (plan §4.5).
        group.MapGet("/", async (int botId, BotFactoryDbContext db) =>
        {
            var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == botId);
            if (bot is null)
                return Results.NotFound();

            var rows = await db.ToolDefinitions
                .AsNoTracking()
                .Where(d => d.Status == ToolDefinitionStatus.Published
                    || db.BotTools.Any(bt => bt.BotId == botId && bt.ToolDefinitionId == d.Id))
                .OrderBy(d => d.DisplayName)
                .Select(d => new
                {
                    Definition = d,
                    Association = db.BotTools
                        .Where(bt => bt.BotId == botId && bt.ToolDefinitionId == d.Id)
                        .FirstOrDefault(),
                })
                .ToListAsync();

            var response = rows.Select(x => new BotToolResponse(
                x.Definition.Id,
                x.Definition.Name,
                x.Definition.DisplayName,
                x.Definition.Category,
                x.Definition.Status.ToString(),
                x.Definition.DataClassification.ToString(),
                x.Definition.RequiresAuthenticatedUser,
                x.Association?.Enabled ?? false,
                x.Association?.AdditionalInstructions,
                x.Association?.MaxCallsPerConversationOverride)).ToList();

            return Results.Ok(response);
        })
        .WithName("GetBotTools")
        .Produces<List<BotToolResponse>>()
        .Produces(404);

        // Upserts the listed associations; tools not listed stay unchanged.
        group.MapPut("/", async (
            int botId,
            UpdateBotToolsRequest request,
            BotFactoryDbContext db) =>
        {
            var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == botId);
            if (bot is null)
                return Results.NotFound();

            var toolIds = request.Tools.Select(t => t.ToolDefinitionId).Distinct().ToList();
            var definitions = await db.ToolDefinitions
                .Where(d => toolIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id);

            var missing = toolIds.Where(id => !definitions.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                return Results.BadRequest(new { error = "Herramientas inexistentes.", toolDefinitionIds = missing });

            var existing = await db.BotTools
                .Where(bt => bt.BotId == botId && toolIds.Contains(bt.ToolDefinitionId))
                .ToDictionaryAsync(bt => bt.ToolDefinitionId);

            foreach (var upsert in request.Tools)
            {
                if (existing.TryGetValue(upsert.ToolDefinitionId, out var association))
                {
                    association.Enabled = upsert.Enabled;
                    association.AdditionalInstructions = string.IsNullOrWhiteSpace(upsert.AdditionalInstructions)
                        ? null
                        : upsert.AdditionalInstructions;
                    association.MaxCallsPerConversationOverride =
                        upsert.MaxCallsPerConversationOverride is <= 0
                            ? null
                            : upsert.MaxCallsPerConversationOverride;
                }
                else if (upsert.Enabled || !string.IsNullOrWhiteSpace(upsert.AdditionalInstructions))
                {
                    db.BotTools.Add(new BotTool
                    {
                        BotId = botId,
                        ToolDefinitionId = upsert.ToolDefinitionId,
                        Enabled = upsert.Enabled,
                        AdditionalInstructions = string.IsNullOrWhiteSpace(upsert.AdditionalInstructions)
                            ? null
                            : upsert.AdditionalInstructions,
                        MaxCallsPerConversationOverride = upsert.MaxCallsPerConversationOverride is <= 0
                            ? null
                            : upsert.MaxCallsPerConversationOverride,
                    });
                }
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequirePermission(Permissions.BotUpdate)
        .WithName("UpdateBotTools")
        .Produces(204)
        .Produces(400)
        .Produces(404);
    }
}
