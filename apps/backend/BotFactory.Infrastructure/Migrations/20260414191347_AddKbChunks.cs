using System;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKbChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kb_chunks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KbDocumentId = table.Column<int>(type: "int", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Embedding = table.Column<SqlVector<float>>(type: "vector(1536)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kb_chunks_kb_documents_KbDocumentId",
                        column: x => x.KbDocumentId,
                        principalTable: "kb_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kb_chunks_KbDocumentId",
                table: "kb_chunks",
                column: "KbDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_kb_chunks_OrganizationId",
                table: "kb_chunks",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kb_chunks");
        }
    }
}
