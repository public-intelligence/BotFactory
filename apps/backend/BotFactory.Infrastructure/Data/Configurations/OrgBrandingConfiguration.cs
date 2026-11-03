using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class OrgBrandingConfiguration : IEntityTypeConfiguration<OrgBranding>
{
    public void Configure(EntityTypeBuilder<OrgBranding> builder)
    {
        builder.ToTable("org_branding");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(b => b.LogoBlobKey).HasMaxLength(500);
        builder.Property(b => b.LogoFilename).HasMaxLength(255);
        builder.Property(b => b.FaviconBlobKey).HasMaxLength(500);
        builder.Property(b => b.FaviconFilename).HasMaxLength(255);

        builder.HasIndex(b => b.OrganizationId).IsUnique();
    }
}
