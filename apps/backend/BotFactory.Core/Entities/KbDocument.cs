namespace BotFactory.Core.Entities;

public class KbDocument : BaseEntity, IAuditable
{
    public int BotId { get; set; }
    public string BlobKey { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public KbDocumentSourceType SourceType { get; set; } = KbDocumentSourceType.File;
    public string? SourceUrl { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime? IndexedAt { get; set; }
}
