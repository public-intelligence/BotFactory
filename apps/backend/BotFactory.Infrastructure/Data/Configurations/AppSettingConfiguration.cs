using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.ServiceType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Provider).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.EncryptedApiKey).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(a => a.KeyPrefix).IsRequired().HasMaxLength(10);
        builder.Property(a => a.OrganizationId).IsRequired().HasMaxLength(50);

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => new { a.OrganizationId, a.ServiceType }).IsUnique();
    }
}
