using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.ApplicationDbContext.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressRetryAndSavedDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChainRetryFromId",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChainRetryPreviousStatus",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgressMessage",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProgressPercent",
                schema: "ticker",
                table: "TimeTickers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProgressUpdatedAt",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgressMessage",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProgressPercent",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProgressUpdatedAt",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TickerQDefinitions",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Target = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    IncludesRequestData = table.Column<bool>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivationToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    PendingRootTickerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActivationClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActivatedRootTickerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQDefinitions", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TickerQDefinitions_List",
                schema: "ticker",
                table: "TickerQDefinitions",
                columns: new[] { "ApplicationNamespaceKey", "Kind", "Target", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_TickerQDefinitions_Name",
                schema: "ticker",
                table: "TickerQDefinitions",
                columns: new[] { "ApplicationNamespaceKey", "NameKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TickerQDefinitions",
                schema: "ticker");

            migrationBuilder.DropColumn(
                name: "ChainRetryFromId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ChainRetryPreviousStatus",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ProgressMessage",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ProgressPercent",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ProgressUpdatedAt",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ProgressMessage",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "ProgressPercent",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "ProgressUpdatedAt",
                schema: "ticker",
                table: "CronTickerOccurrences");
        }
    }
}
