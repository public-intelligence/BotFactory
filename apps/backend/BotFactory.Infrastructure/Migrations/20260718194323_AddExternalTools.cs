using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tool_connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConnectorType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AuthType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AuthConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EncryptedSecretsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SecretPrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DefaultHeadersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeoutMs = table.Column<int>(type: "int", nullable: false),
                    MaxResponseBytes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tool_executions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BotId = table.Column<int>(type: "int", nullable: true),
                    ConversationId = table.Column<int>(type: "int", nullable: true),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ToolDefinitionId = table.Column<int>(type: "int", nullable: false),
                    ToolConnectionId = table.Column<int>(type: "int", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConnectorType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    ErrorCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedRequestSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ResponseBytes = table.Column<long>(type: "bigint", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    ResultSnippetJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefinitionSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_executions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tool_definitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ToolConnectionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupportContact = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AiDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AiUsageNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PathTemplate = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    QueryParamsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StaticHeadersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BodyTemplateJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParametersSchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseMappingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMappingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsConsequential = table.Column<bool>(type: "bit", nullable: false),
                    RequiresUserConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    RequiresAuthenticatedUser = table.Column<bool>(type: "bit", nullable: false),
                    MaxCallsPerConversation = table.Column<int>(type: "int", nullable: true),
                    TimeoutMsOverride = table.Column<int>(type: "int", nullable: true),
                    DataClassification = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastTestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastTestOutcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tool_definitions_tool_connections_ToolConnectionId",
                        column: x => x.ToolConnectionId,
                        principalTable: "tool_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bot_tools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BotId = table.Column<int>(type: "int", nullable: false),
                    ToolDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    AdditionalInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MaxCallsPerConversationOverride = table.Column<int>(type: "int", nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_tools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bot_tools_bots_BotId",
                        column: x => x.BotId,
                        principalTable: "bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bot_tools_tool_definitions_ToolDefinitionId",
                        column: x => x.ToolDefinitionId,
                        principalTable: "tool_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bot_tools_BotId_ToolDefinitionId",
                table: "bot_tools",
                columns: new[] { "BotId", "ToolDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bot_tools_OrganizationId",
                table: "bot_tools",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_bot_tools_ToolDefinitionId",
                table: "bot_tools",
                column: "ToolDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_connections_Name_OrganizationId",
                table: "tool_connections",
                columns: new[] { "Name", "OrganizationId" },
                unique: true,
                filter: "[Name] != ''");

            migrationBuilder.CreateIndex(
                name: "IX_tool_connections_OrganizationId",
                table: "tool_connections",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_definitions_Name_OrganizationId",
                table: "tool_definitions",
                columns: new[] { "Name", "OrganizationId" },
                unique: true,
                filter: "[Name] != ''");

            migrationBuilder.CreateIndex(
                name: "IX_tool_definitions_OrganizationId",
                table: "tool_definitions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_definitions_ToolConnectionId",
                table: "tool_definitions",
                column: "ToolConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_executions_ConversationId",
                table: "tool_executions",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_executions_OrganizationId_ToolDefinitionId_CreatedAt",
                table: "tool_executions",
                columns: new[] { "OrganizationId", "ToolDefinitionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_tools");

            migrationBuilder.DropTable(
                name: "tool_executions");

            migrationBuilder.DropTable(
                name: "tool_definitions");

            migrationBuilder.DropTable(
                name: "tool_connections");
        }
    }
}
