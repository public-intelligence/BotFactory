using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class TelemetryEventConfiguration : IEntityTypeConfiguration<TelemetryEvent>
{
    public void Configure(EntityTypeBuilder<TelemetryEvent> builder)
    {
        builder.ToTable("telemetry_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.EventName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Source).HasConversion<int>();
        builder.Property(e => e.UserId).HasMaxLength(100);
        builder.Property(e => e.Stage).HasMaxLength(50);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.Summary).HasMaxLength(500);
        builder.Property(e => e.Data).HasColumnType("nvarchar(max)");

        builder.HasIndex(e => new { e.OrganizationId, e.OccurredAt })
            .HasDatabaseName("IX_telemetry_events_org_occurred");
        builder.HasIndex(e => new { e.ConversationId, e.OccurredAt })
            .HasDatabaseName("IX_telemetry_events_conv_occurred");
        builder.HasIndex(e => new { e.EventName, e.OccurredAt })
            .HasDatabaseName("IX_telemetry_events_name_occurred");
        builder.HasIndex(e => e.TurnId)
            .HasDatabaseName("IX_telemetry_events_turn");
    }
}
