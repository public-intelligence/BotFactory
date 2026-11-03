using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "org_branding",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogoBlobKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LogoFilename = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    FaviconBlobKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FaviconFilename = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_branding", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_org_branding_OrganizationId",
                table: "org_branding",
                column: "OrganizationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "org_branding");
        }
    }
}
