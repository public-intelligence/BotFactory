using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class KbDocumentConfiguration : IEntityTypeConfiguration<KbDocument>
{
    public void Configure(EntityTypeBuilder<KbDocument> builder)
    {
        builder.ToTable("kb_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.BotId).IsRequired();
        builder.Property(d => d.BlobKey).IsRequired().HasMaxLength(500);
        builder.Property(d => d.Filename).IsRequired().HasMaxLength(2048);
        builder.Property(d => d.SourceType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.SourceUrl).HasMaxLength(2048);
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ErrorMessage).HasMaxLength(1000);
        builder.Property(d => d.OrganizationId).IsRequired().HasMaxLength(50);

        builder.HasIndex(d => d.OrganizationId);
        builder.HasIndex(d => d.BotId);
        builder.HasIndex(d => d.SourceType);
        builder.HasIndex(d => new { d.BotId, d.SourceUrl })
            .IsUnique()
            .HasFilter("[SourceUrl] IS NOT NULL");

        builder.HasOne<Bot>().WithMany().HasForeignKey(d => d.BotId).OnDelete(DeleteBehavior.Cascade);
    }
}
