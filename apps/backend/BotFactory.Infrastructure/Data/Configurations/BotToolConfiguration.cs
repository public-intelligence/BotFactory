using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class BotToolConfiguration : IEntityTypeConfiguration<BotTool>
{
    public void Configure(EntityTypeBuilder<BotTool> builder)
    {
        builder.ToTable("bot_tools");

        builder.HasKey(bt => bt.Id);
        builder.Property(bt => bt.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(bt => bt.AdditionalInstructions).HasMaxLength(2000);

        builder.HasOne(bt => bt.Bot)
            .WithMany()
            .HasForeignKey(bt => bt.BotId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: deleting a tool with bot associations is blocked at the API
        // with a 409 listing dependent bots; associations must go first.
        builder.HasOne(bt => bt.ToolDefinition)
            .WithMany()
            .HasForeignKey(bt => bt.ToolDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(bt => bt.OrganizationId);
        builder.HasIndex(bt => new { bt.BotId, bt.ToolDefinitionId }).IsUnique();
    }
}
