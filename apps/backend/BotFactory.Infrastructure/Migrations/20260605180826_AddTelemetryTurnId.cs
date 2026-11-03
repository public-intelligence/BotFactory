using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryTurnId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TurnId",
                table: "telemetry_events",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_turn",
                table: "telemetry_events",
                column: "TurnId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_turn",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "TurnId",
                table: "telemetry_events");
        }
    }
}
