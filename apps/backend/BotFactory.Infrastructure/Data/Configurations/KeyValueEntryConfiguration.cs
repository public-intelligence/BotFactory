using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class KeyValueEntryConfiguration : IEntityTypeConfiguration<KeyValueEntry>
{
    public void Configure(EntityTypeBuilder<KeyValueEntry> builder)
    {
        builder.ToTable("key_value_entries");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Key).IsRequired().HasMaxLength(200);
        builder.Property(k => k.Value).IsRequired().HasColumnType("json");
        builder.Property(k => k.OrganizationId).IsRequired().HasMaxLength(50);

        builder.HasIndex(k => new { k.OrganizationId, k.Key }).IsUnique();
    }
}
