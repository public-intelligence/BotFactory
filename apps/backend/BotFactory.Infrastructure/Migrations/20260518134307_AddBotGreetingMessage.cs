using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBotGreetingMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing bots with the default Spanish greeting so the feature is
            // on by default after deploy. Operators can clear the field in the UI to
            // disable the greeting per bot.
            migrationBuilder.AddColumn<string>(
                name: "GreetingMessage",
                table: "bots",
                type: "nvarchar(max)",
                nullable: true,
                defaultValue: "¡Hola! Estoy para servirte.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GreetingMessage",
                table: "bots");
        }
    }
}
