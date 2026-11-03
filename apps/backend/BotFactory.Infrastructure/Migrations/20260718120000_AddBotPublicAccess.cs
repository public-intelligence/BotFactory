using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    [DbContext(typeof(Data.BotFactoryDbContext))]
    [Migration("20260718120000_AddBotPublicAccess")]
    public partial class AddBotPublicAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // All existing bots stay login-required (backward compatible).
            migrationBuilder.AddColumn<bool>(
                name: "RequiresAuthentication",
                table: "bots",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "bots",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "EmbedEnabled",
                table: "bots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AllowedEmbedOrigins",
                table: "bots",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            // Backfill: every existing bot gets an opaque public id so links can
            // be generated without re-saving the bot. NEWID() is CSPRNG-backed;
            // 32 hex chars ≈ 122 bits of entropy, same non-enumerable property
            // as the base62 ids the app generates for new bots.
            migrationBuilder.Sql("""
                UPDATE bots
                SET PublicId = LOWER(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''))
                WHERE PublicId = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_bots_PublicId",
                table: "bots",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bots_PublicId",
                table: "bots");

            migrationBuilder.DropColumn(name: "RequiresAuthentication", table: "bots");
            migrationBuilder.DropColumn(name: "PublicId", table: "bots");
            migrationBuilder.DropColumn(name: "EmbedEnabled", table: "bots");
            migrationBuilder.DropColumn(name: "AllowedEmbedOrigins", table: "bots");
        }
    }
}
