using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class ToolExecutionConfiguration : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> builder)
    {
        builder.ToTable("tool_executions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.UserId).HasMaxLength(100);
        builder.Property(e => e.ToolName).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ConnectorType).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(e => e.ArgumentsJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(e => e.Outcome).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(e => e.ErrorCategory).HasMaxLength(100);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.ResolvedRequestSummary).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.ResultSnippetJson).HasColumnType("nvarchar(max)");
        builder.Property(e => e.DefinitionSnapshotJson).IsRequired().HasColumnType("nvarchar(max)");

        // Telemetry-style append-only log: intentionally no FK constraints so
        // history survives tool/connection/bot deletion (names denormalized).
        builder.HasIndex(e => new { e.OrganizationId, e.ToolDefinitionId, e.CreatedAt });
        builder.HasIndex(e => e.ConversationId);
    }
}
