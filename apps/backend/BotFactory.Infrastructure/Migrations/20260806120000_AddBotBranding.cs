using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    [DbContext(typeof(Data.BotFactoryDbContext))]
    [Migration("20260806120000_AddBotBranding")]
    public partial class AddBotBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThemeBackground",
                table: "bots",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemePrimary",
                table: "bots",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeAccent",
                table: "bots",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoBlobKey",
                table: "bots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoFilename",
                table: "bots",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ThemeBackground", table: "bots");
            migrationBuilder.DropColumn(name: "ThemePrimary", table: "bots");
            migrationBuilder.DropColumn(name: "ThemeAccent", table: "bots");
            migrationBuilder.DropColumn(name: "LogoBlobKey", table: "bots");
            migrationBuilder.DropColumn(name: "LogoFilename", table: "bots");
        }
    }
}
