using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.ConversationId).IsRequired();
        builder.Property(m => m.Role).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Content).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(m => m.Provider).HasMaxLength(100);
        builder.Property(m => m.OrganizationId).IsRequired().HasMaxLength(50);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });

        builder.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
