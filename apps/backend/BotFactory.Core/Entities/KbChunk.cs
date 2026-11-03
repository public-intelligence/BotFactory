using Microsoft.Data.SqlTypes;

namespace BotFactory.Core.Entities;

public class KbChunk : BaseEntity
{
    public int KbDocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SourceLocation { get; set; } = string.Empty;
    public SqlVector<float> Embedding { get; set; }
}
