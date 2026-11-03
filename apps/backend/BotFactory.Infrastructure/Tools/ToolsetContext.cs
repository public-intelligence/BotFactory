using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Tools;

/// <summary>
/// Scoped holder for the current turn's bot + its resolved configured tools.
/// Mirrors the TenantContext setter idiom: ChatTurnService calls SetBotAsync
/// right after SetTenant; ToolRegistry then composes synchronously from
/// EnabledTools. Paths that never call SetBotAsync see an empty list and the
/// registry behaves exactly as before this feature (built-ins only).
/// </summary>
public class ToolsetContext : IToolsetContext, IToolsetContextSetter
{
    private readonly BotFactoryDbContext _db;
    private IReadOnlyList<ResolvedBotTool> _enabledTools = [];

    public ToolsetContext(BotFactoryDbContext db)
    {
        _db = db;
    }

    public Bot? Bot { get; private set; }

    public IReadOnlyList<ResolvedBotTool> EnabledTools => _enabledTools;

    public async Task SetBotAsync(Bot bot, CancellationToken cancellationToken)
    {
        Bot = bot;

        // Tenant query filters apply (SetTenant must run first). Status checks
        // here are the fast path; the execution gateway re-checks them per call.
        var rows = await _db.BotTools
            .AsNoTracking()
            .Include(bt => bt.ToolDefinition!)
            .ThenInclude(d => d.Connection)
            .Where(bt => bt.BotId == bot.Id
                && bt.Enabled
                && bt.ToolDefinition!.Status == ToolDefinitionStatus.Published
                && bt.ToolDefinition!.Connection!.Status == ToolConnectionStatus.Active)
            .OrderBy(bt => bt.ToolDefinition!.Name)
            .ToListAsync(cancellationToken);

        _enabledTools = rows
            .Where(bt => bt.ToolDefinition?.Connection is not null)
            .Select(bt => new ResolvedBotTool(bt.ToolDefinition!, bt.ToolDefinition!.Connection!, bt))
            .ToList();
    }
}
