using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    [DbContext(typeof(Data.BotFactoryDbContext))]
    [Migration("20260714150000_UniqueKbDocumentSourceUrlPerBot")]
    public partial class UniqueKbDocumentSourceUrlPerBot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the oldest document per bot+URL; remove later duplicates.
            migrationBuilder.Sql("""
                WITH dups AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY BotId, SourceUrl ORDER BY Id) AS rn
                    FROM kb_documents
                    WHERE SourceUrl IS NOT NULL
                )
                DELETE FROM kb_documents
                WHERE Id IN (SELECT Id FROM dups WHERE rn > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_kb_documents_BotId_SourceUrl",
                table: "kb_documents",
                columns: new[] { "BotId", "SourceUrl" },
                unique: true,
                filter: "[SourceUrl] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_kb_documents_BotId_SourceUrl",
                table: "kb_documents");
        }
    }
}
