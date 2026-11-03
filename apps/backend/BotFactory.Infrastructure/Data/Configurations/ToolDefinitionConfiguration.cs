using BotFactory.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotFactory.Infrastructure.Data.Configurations;

public class ToolDefinitionConfiguration : IEntityTypeConfiguration<ToolDefinition>
{
    public void Configure(EntityTypeBuilder<ToolDefinition> builder)
    {
        builder.ToTable("tool_definitions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.OrganizationId).IsRequired().HasMaxLength(50);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(64);
        builder.Property(d => d.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Category).HasMaxLength(100);
        builder.Property(d => d.SupportContact).HasMaxLength(200);
        builder.Property(d => d.AiDescription).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(d => d.AiUsageNotes).HasColumnType("nvarchar(max)");
        builder.Property(d => d.HttpMethod).IsRequired().HasMaxLength(10);
        builder.Property(d => d.PathTemplate).IsRequired().HasMaxLength(1000);
        builder.Property(d => d.QueryParamsJson).HasColumnType("nvarchar(max)");
        builder.Property(d => d.StaticHeadersJson).HasColumnType("nvarchar(max)");
        builder.Property(d => d.BodyTemplateJson).HasColumnType("nvarchar(max)");
        builder.Property(d => d.ParametersSchemaJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(d => d.ResponseMappingJson).HasColumnType("nvarchar(max)");
        builder.Property(d => d.ErrorMappingJson).HasColumnType("nvarchar(max)");
        builder.Property(d => d.DataClassification).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(d => d.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(d => d.LastTestOutcome).HasMaxLength(20);

        builder.HasOne(d => d.Connection)
            .WithMany()
            .HasForeignKey(d => d.ToolConnectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.OrganizationId);
        // The LLM function name must be unambiguous within an organization.
        builder.HasIndex(d => new { d.Name, d.OrganizationId })
            .IsUnique()
            .HasFilter("[Name] != ''");
    }
}
