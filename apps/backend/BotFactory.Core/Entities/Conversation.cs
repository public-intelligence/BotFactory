namespace BotFactory.Core.Entities;

public class Conversation : BaseEntity
{
    public int BotId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;
    public DateTime? PausedAt { get; set; }
    public string? PausedReason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedReason { get; set; }

    // Observability for public access (planning/anonymous-access-embedding-plan.md §10).
    // AccessMode/Surface are set at creation and never change; SourceUrl and
    // ParentOrigin are client-reported (best-effort, query strings stripped)
    // so dashboards can split direct vs embedded usage per hosting domain.
    public string AccessMode { get; set; } = ConversationAccessModes.Authenticated;
    public string Surface { get; set; } = ConversationSurfaces.Direct;
    public string? SourceUrl { get; set; }
    public string? ParentOrigin { get; set; }
}
