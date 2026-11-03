using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetrySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "telemetry_events",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Summary",
                table: "telemetry_events");
        }
    }
}
