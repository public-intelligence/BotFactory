using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBotInteractionModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableChat",
                table: "bots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableVoice",
                table: "bots",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.DropColumn(
                name: "EnableTextOnlyFallback",
                table: "bots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableTextOnlyFallback",
                table: "bots",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.DropColumn(
                name: "EnableVoice",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "EnableChat",
                table: "bots");
        }
    }
}
