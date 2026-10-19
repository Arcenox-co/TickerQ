using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.Console.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringCronChains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OriginCronTickerId",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Chain",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChainRootId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeTickers_ApplicationNamespaceKey_OriginCronTickerId_Status",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "OriginCronTickerId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeTickers_ApplicationNamespaceKey_OriginCronTickerId_Status",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "OriginCronTickerId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "Chain",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "ChainRootId",
                schema: "ticker",
                table: "CronTickerOccurrences");
        }
    }
}
