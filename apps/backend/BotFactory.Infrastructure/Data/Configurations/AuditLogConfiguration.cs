using System.Text.Json;
using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(20);
        builder.Property(a => a.UserId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.UserName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.OrganizationId).IsRequired().HasMaxLength(50);

        builder.Property(a => a.Changes)
            .HasColumnType("json")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<AuditFieldChange>>(v, JsonOptions) ?? new List<AuditFieldChange>(),
                new ValueComparer<List<AuditFieldChange>>(
                    (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                    v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<AuditFieldChange>>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.CreatedAt);
    }
}
