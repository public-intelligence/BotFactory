using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UrlSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PublishedState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MasterPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopK = table.Column<int>(type: "int", nullable: false),
                    SilenceEndMs = table.Column<int>(type: "int", nullable: false),
                    NoSpeechTimeoutMs = table.Column<int>(type: "int", nullable: false),
                    MaxUtteranceMs = table.Column<int>(type: "int", nullable: false),
                    EnableBargeIn = table.Column<bool>(type: "bit", nullable: false),
                    EnableTts = table.Column<bool>(type: "bit", nullable: false),
                    EnableTypingSound = table.Column<bool>(type: "bit", nullable: false),
                    EnableTextOnlyFallback = table.Column<bool>(type: "bit", nullable: false),
                    GroundedOnlyMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bots_OrganizationId",
                table: "bots",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_bots_UrlSlug_OrganizationId",
                table: "bots",
                columns: new[] { "UrlSlug", "OrganizationId" },
                unique: true,
                filter: "[UrlSlug] != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bots");
        }
    }
}
