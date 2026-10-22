using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.Console.Migrations
{
    /// <inheritdoc />
    public partial class AddCompleteTickerQSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantConversations",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantInvestigationBudgets",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespace = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    StartedAtTicks = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantInvestigationBudgets", x => x.ApplicationNamespace);
                });

            migrationBuilder.CreateTable(
                name: "AssistantInvestigationLeases",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespace = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OwnerToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantInvestigationLeases", x => new { x.ApplicationNamespace, x.EvidenceFingerprint });
                });

            migrationBuilder.CreateTable(
                name: "AssistantInvestigations",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationNamespace = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChainRootId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FunctionName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ExecutionStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Classification = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReviewState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceRepository = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SourceRevision = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SourceLine = table.Column<int>(type: "INTEGER", nullable: true),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedByUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RecoveredByExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecoveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantInvestigations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionLogs",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PartitionKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Truncated = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantMessages",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TokensIn = table.Column<int>(type: "INTEGER", nullable: true),
                    TokensOut = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantMessages_AssistantConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "ticker",
                        principalTable: "AssistantConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversation_UserId_UpdatedAt",
                schema: "ticker",
                table: "AssistantConversations",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigationLease_LeaseUntil",
                schema: "ticker",
                table: "AssistantInvestigationLeases",
                column: "LeaseUntil");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigation_Namespace_Evidence_CompletedAt",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "EvidenceFingerprint", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigation_Namespace_Function_OccurredAt",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "FunctionName", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigation_Namespace_UpdatedAt",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AssistantInvestigation_Namespace_Execution",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "ExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessage_ConversationId_Ordinal",
                schema: "ticker",
                table: "AssistantMessages",
                columns: new[] { "ConversationId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLogs_PartitionKey_ExpiresAtUtcTicks",
                schema: "ticker",
                table: "ExecutionLogs",
                columns: new[] { "PartitionKey", "ExpiresAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLogs_PartitionKey_UpdatedAtUtcTicks_Id",
                schema: "ticker",
                table: "ExecutionLogs",
                columns: new[] { "PartitionKey", "UpdatedAtUtcTicks", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantInvestigationBudgets",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantInvestigationLeases",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantInvestigations",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantMessages",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "ExecutionLogs",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantConversations",
                schema: "ticker");
        }
    }
}
