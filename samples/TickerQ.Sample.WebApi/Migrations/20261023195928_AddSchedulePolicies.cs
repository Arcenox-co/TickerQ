using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlockedOccurrenceBehavior",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaximumLateness",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissedExecutionAction",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleAdjustmentReason",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchedulePolicyName",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SchedulePolicyVersion",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlockedOccurrenceBehavior",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CatchUpMode",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "MaximumLateness",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissedExecutionAction",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SchedulePolicyName",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SchedulePolicyVersion",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchedulePolicies",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePolicies", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_SchedulePolicy",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "SchedulePolicyName" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_SchedulePolicy",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "SchedulePolicyName" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePolicies_Updated",
                schema: "ticker",
                table: "SchedulePolicies",
                columns: new[] { "ApplicationNamespaceKey", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SchedulePolicies_Name",
                schema: "ticker",
                table: "SchedulePolicies",
                columns: new[] { "ApplicationNamespaceKey", "NameKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulePolicies",
                schema: "ticker");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_SchedulePolicy",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_CronTickers_SchedulePolicy",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "BlockedOccurrenceBehavior",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "MaximumLateness",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "MissedExecutionAction",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "RequestedExecutionTime",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ScheduleAdjustmentReason",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "SchedulePolicyName",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "SchedulePolicyVersion",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "BlockedOccurrenceBehavior",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "CatchUpMode",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "MaximumLateness",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "MissedExecutionAction",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "SchedulePolicyName",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "SchedulePolicyVersion",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                schema: "ticker",
                table: "CronTickers");
        }
    }
}
