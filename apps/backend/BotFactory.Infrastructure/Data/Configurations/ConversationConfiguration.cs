using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.BotId).IsRequired();
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(450);
        builder.Property(c => c.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.PausedReason).HasMaxLength(2000);
        builder.Property(c => c.ClosedReason).HasMaxLength(2000);
        builder.Property(c => c.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(c => c.AccessMode).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Surface).IsRequired().HasMaxLength(20);
        builder.Property(c => c.SourceUrl).HasMaxLength(2000);
        builder.Property(c => c.ParentOrigin).HasMaxLength(500);

        builder.HasIndex(c => c.OrganizationId);
        builder.HasIndex(c => c.BotId);
        builder.HasIndex(c => c.UserId);

        builder.HasOne<Bot>().WithMany().HasForeignKey(c => c.BotId).OnDelete(DeleteBehavior.Cascade);
    }
}
