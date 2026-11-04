using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every conversation that exists today becomes "voice", the channel that keeps the
            // old idle rule (twice the bot's no-speech timeout). New sessions set their own.
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "conversations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "voice");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channel",
                table: "conversations");
        }
    }
}
