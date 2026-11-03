using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    [DbContext(typeof(Data.BotFactoryDbContext))]
    [Migration("20260714120000_AddKbDocumentSourceType")]
    public partial class AddKbDocumentSourceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Filename",
                table: "kb_documents",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "kb_documents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "File");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "kb_documents",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_kb_documents_SourceType",
                table: "kb_documents",
                column: "SourceType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_kb_documents_SourceType",
                table: "kb_documents");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "kb_documents");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "kb_documents");

            migrationBuilder.AlterColumn<string>(
                name: "Filename",
                table: "kb_documents",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2048)",
                oldMaxLength: 2048);
        }
    }
}
