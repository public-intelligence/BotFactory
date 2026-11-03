using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class ToolConnectionConfiguration : IEntityTypeConfiguration<ToolConnection>
{
    public void Configure(EntityTypeBuilder<ToolConnection> builder)
    {
        builder.ToTable("tool_connections");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.ConnectorType).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(c => c.BaseUrl).IsRequired().HasMaxLength(500);
        builder.Property(c => c.AuthType).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(c => c.AuthConfigJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(c => c.EncryptedSecretsJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(c => c.SecretPrefix).IsRequired().HasMaxLength(10);
        builder.Property(c => c.DefaultHeadersJson).HasColumnType("nvarchar(max)");
        builder.Property(c => c.Status).HasConversion<string>().IsRequired().HasMaxLength(50);

        builder.HasIndex(c => c.OrganizationId);
        builder.HasIndex(c => new { c.Name, c.OrganizationId })
            .IsUnique()
            .HasFilter("[Name] != ''");
    }
}
