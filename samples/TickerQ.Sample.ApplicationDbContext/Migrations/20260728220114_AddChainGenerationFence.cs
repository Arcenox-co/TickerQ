using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.ApplicationDbContext.Migrations
{
    /// <inheritdoc />
    public partial class AddChainGenerationFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChainGeneration",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChainRootId",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CronTickerOccurrenceResults",
                schema: "ticker",
                columns: table => new
                {
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", maxLength: 1048576, nullable: false),
                    EnvelopeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ContractId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ContractType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickerOccurrenceResults", x => x.TickerId);
                    table.ForeignKey(
                        name: "FK_CronTickerOccurrenceResults_CronTickerOccurrences_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "ticker",
                        principalTable: "CronTickerOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeTickerResults",
                schema: "ticker",
                columns: table => new
                {
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", maxLength: 1048576, nullable: false),
                    EnvelopeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ContractId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ContractType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTickerResults", x => x.TickerId);
                    table.ForeignKey(
                        name: "FK_TimeTickerResults_TimeTickers_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "ticker",
                        principalTable: "TimeTickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ChainRootId",
                schema: "ticker",
                table: "TimeTickers",
                column: "ChainRootId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutedAt",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "Status", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutedAt",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "Status", "ExecutedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CronTickerOccurrenceResults",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TimeTickerResults",
                schema: "ticker");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_ChainRootId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_Status_ExecutedAt",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutedAt",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "ChainGeneration",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ChainRootId",
                schema: "ticker",
                table: "TimeTickers");
        }
    }
}
