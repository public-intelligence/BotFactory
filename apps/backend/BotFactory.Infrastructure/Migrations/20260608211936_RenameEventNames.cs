using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameEventNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE telemetry_events SET EventName = 'backend_completed' WHERE EventName = 'turn_completed';");
            migrationBuilder.Sql(
                "UPDATE telemetry_events SET EventName = 'turn_completed' WHERE EventName = 'turn_metrics';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE telemetry_events SET EventName = 'turn_metrics' WHERE EventName = 'turn_completed';");
            migrationBuilder.Sql(
                "UPDATE telemetry_events SET EventName = 'turn_completed' WHERE EventName = 'backend_completed';");
        }
    }
}
