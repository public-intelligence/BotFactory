using BotFactory.Core.Entities;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class KbChunkConfiguration : IEntityTypeConfiguration<KbChunk>
{
    public void Configure(EntityTypeBuilder<KbChunk> builder)
    {
        builder.ToTable("kb_chunks");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.KbDocumentId).IsRequired();
        builder.Property(c => c.ChunkIndex).IsRequired();
        builder.Property(c => c.Text).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(c => c.SourceLocation).IsRequired().HasMaxLength(200);
        builder.Property(c => c.OrganizationId).IsRequired().HasMaxLength(50);

        builder.Property(c => c.Embedding)
            .IsRequired()
            .HasColumnType("vector(1536)");

        builder.HasIndex(c => c.OrganizationId);
        builder.HasIndex(c => c.KbDocumentId);

        builder.HasOne<KbDocument>().WithMany().HasForeignKey(c => c.KbDocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
