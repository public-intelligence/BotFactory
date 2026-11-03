using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBotVadParams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "VadActivationThreshold",
                table: "bots",
                type: "float",
                nullable: false,
                defaultValue: 0.6);

            migrationBuilder.AddColumn<int>(
                name: "VadMinSpeechDurationMs",
                table: "bots",
                type: "int",
                nullable: false,
                defaultValue: 200);

            // Bump existing rows from the old defaults to the new office-profile defaults.
            // Conditional on the old default value so any bot that was already tuned by a
            // human stays as-is — we only rewrite untouched rows.
            migrationBuilder.Sql("UPDATE bots SET SilenceEndMs = 700 WHERE SilenceEndMs = 1000;");
            migrationBuilder.Sql("UPDATE bots SET NoSpeechTimeoutMs = 15000 WHERE NoSpeechTimeoutMs = 10000;");
            migrationBuilder.Sql("UPDATE bots SET MaxUtteranceMs = 20000 WHERE MaxUtteranceMs = 30000;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert the office-profile bumps back to the old defaults (only for rows that match
            // the new defaults — any further hand-tuning stays in place).
            migrationBuilder.Sql("UPDATE bots SET SilenceEndMs = 1000 WHERE SilenceEndMs = 700;");
            migrationBuilder.Sql("UPDATE bots SET NoSpeechTimeoutMs = 10000 WHERE NoSpeechTimeoutMs = 15000;");
            migrationBuilder.Sql("UPDATE bots SET MaxUtteranceMs = 30000 WHERE MaxUtteranceMs = 20000;");

            migrationBuilder.DropColumn(
                name: "VadActivationThreshold",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "VadMinSpeechDurationMs",
                table: "bots");
        }
    }
}
