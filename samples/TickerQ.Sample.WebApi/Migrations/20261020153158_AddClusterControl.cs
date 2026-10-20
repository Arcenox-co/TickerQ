using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddClusterControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TickerQControlCommands",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetNodeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetExecutionOwnerId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TickerType = table.Column<int>(type: "INTEGER", nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcquisitionToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQControlCommands", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "TickerQNodes",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NodeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionOwnerId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NodeIdentifier = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DashboardUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxConcurrency = table.Column<int>(type: "INTEGER", nullable: false),
                    FunctionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveExecutions = table.Column<int>(type: "INTEGER", nullable: false),
                    QueuedExecutions = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveWorkflows = table.Column<int>(type: "INTEGER", nullable: false),
                    WaitingForFunction = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryingExecutions = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQNodes", x => new { x.ApplicationNamespaceKey, x.NodeKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TickerQControlCommands_ApplicationNamespaceKey_ExpiresAtUtc",
                schema: "ticker",
                table: "TickerQControlCommands",
                columns: new[] { "ApplicationNamespaceKey", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TickerQControlCommands_ApplicationNamespaceKey_TargetNodeKey_Status_RequestedAtUtc",
                schema: "ticker",
                table: "TickerQControlCommands",
                columns: new[] { "ApplicationNamespaceKey", "TargetNodeKey", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TickerQNodes_ApplicationNamespaceKey_LastSeenAtUtc",
                schema: "ticker",
                table: "TickerQNodes",
                columns: new[] { "ApplicationNamespaceKey", "LastSeenAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TickerQControlCommands",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TickerQNodes",
                schema: "ticker");
        }
    }
}
