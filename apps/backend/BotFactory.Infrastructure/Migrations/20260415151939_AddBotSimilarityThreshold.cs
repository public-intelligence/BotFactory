using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBotSimilarityThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SimilarityThreshold",
                table: "bots",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SimilarityThreshold",
                table: "bots");
        }
    }
}
