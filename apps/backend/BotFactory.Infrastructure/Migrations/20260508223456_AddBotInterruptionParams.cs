using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBotInterruptionParams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing bots to the office-profile defaults (1.0s of speech +
            // 1 word required to interrupt) instead of 0/0, which would let any blip cut
            // the bot off — the whole point of these fields.
            migrationBuilder.AddColumn<int>(
                name: "InterruptionMinDurationMs",
                table: "bots",
                type: "int",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "InterruptionMinWords",
                table: "bots",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterruptionMinDurationMs",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "InterruptionMinWords",
                table: "bots");
        }
    }
}
