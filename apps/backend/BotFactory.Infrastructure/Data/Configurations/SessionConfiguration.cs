using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.AccessToken).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(s => s.RefreshToken).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(s => s.AuthProvider).IsRequired().HasMaxLength(50);
        builder.Property(s => s.ExpiresAt).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
    }
}
