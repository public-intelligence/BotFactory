using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class BotConfiguration : IEntityTypeConfiguration<Bot>
{
    public void Configure(EntityTypeBuilder<Bot> builder)
    {
        builder.ToTable("bots");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.BotName).IsRequired().HasMaxLength(200);

        builder.Property(b => b.UrlSlug).IsRequired().HasMaxLength(200);
        builder.Property(b => b.PublishedState).IsRequired().HasMaxLength(50);
        builder.Property(b => b.MasterPrompt).HasColumnType("nvarchar(max)");
        builder.Property(b => b.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(b => b.GroundedOnlyMessage).HasMaxLength(2000);
        builder.Property(b => b.PublicId).IsRequired().HasMaxLength(32);
        builder.Property(b => b.AllowedEmbedOrigins).IsRequired().HasMaxLength(2000);
        // 50 terms of up to 20 characters, one per line (TranscriptionTerms).
        builder.Property(b => b.TranscriptionVocabulary).IsRequired().HasMaxLength(1200);
        // Up to 100 rules, one per line (PronunciationDictionary): well under 8000 characters.
        builder.Property(b => b.PronunciationRules).IsRequired().HasMaxLength(8000);
        builder.Property(b => b.ThemeBackground).HasMaxLength(7);
        builder.Property(b => b.ThemePrimary).HasMaxLength(7);
        builder.Property(b => b.ThemeAccent).HasMaxLength(7);

        builder.HasIndex(b => b.OrganizationId);
        builder.HasIndex(b => new { b.UrlSlug, b.OrganizationId })
            .IsUnique()
            .HasFilter("[UrlSlug] != ''");
        // Globally unique: PublicId is the cross-tenant public/embed key.
        builder.HasIndex(b => b.PublicId)
            .IsUnique()
            .HasFilter("[PublicId] != ''");
    }
}
