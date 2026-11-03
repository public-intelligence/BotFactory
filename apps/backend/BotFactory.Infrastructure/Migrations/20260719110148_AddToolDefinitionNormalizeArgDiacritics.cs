using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddToolDefinitionNormalizeArgDiacritics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NormalizeArgDiacritics",
                table: "tool_definitions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizeArgDiacritics",
                table: "tool_definitions");
        }
    }
}
