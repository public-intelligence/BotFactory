using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    [DbContext(typeof(Data.BotFactoryDbContext))]
    [Migration("20260718160000_AddPublicUsageObservability")]
    public partial class AddPublicUsageObservability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                table: "conversations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "authenticated");

            migrationBuilder.AddColumn<string>(
                name: "Surface",
                table: "conversations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "direct");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "conversations",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentOrigin",
                table: "conversations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Conversations created by anonymous sessions before this column
            // existed are recognizable by their synthetic user id.
            migrationBuilder.Sql("""
                UPDATE conversations
                SET AccessMode = 'public'
                WHERE UserId LIKE 'anon:%';
                """);

            migrationBuilder.AddColumn<int>(
                name: "PublicRequestsPerMinute",
                table: "bots",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AccessMode", table: "conversations");
            migrationBuilder.DropColumn(name: "Surface", table: "conversations");
            migrationBuilder.DropColumn(name: "SourceUrl", table: "conversations");
            migrationBuilder.DropColumn(name: "ParentOrigin", table: "conversations");
            migrationBuilder.DropColumn(name: "PublicRequestsPerMinute", table: "bots");
        }
    }
}
