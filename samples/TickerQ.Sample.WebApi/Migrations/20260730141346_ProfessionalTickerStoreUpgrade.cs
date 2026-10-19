using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ProfessionalTickerStoreUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DefinitionRevision",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAt",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetirementRequestedAt",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeedKey",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SeedLastSeenAt",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SeedManifestEpoch",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeedOwnerNamespace",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeedWasEnabledBeforeRetirement",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DefinitionRevision",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "NodeFinalizationOutbox",
                schema: "ticker",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    TickerType = table.Column<int>(type: "INTEGER", nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcquisitionToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeEpoch = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinalizeUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    FinalizePathAndQuery = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    AllowPrivateCallbackAddressesForLocalDevelopment = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestNonce = table.Column<Guid>(type: "TEXT", nullable: false),
                    ControlNonce = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExactBody = table.Column<byte[]>(type: "BLOB", maxLength: 8192, nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    TerminalMutationDigest = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClaimedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeFinalizationOutbox", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "TickerQStoreMetadata",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DataVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    LastMigrationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivationEpoch = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivationPhase = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivationCheckpoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQStoreMetadata", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_SeedOwner_Epoch",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "SeedOwnerNamespace", "SeedManifestEpoch" });

            migrationBuilder.CreateIndex(
                name: "UX_CronTickers_SeedKey",
                schema: "ticker",
                table: "CronTickers",
                column: "SeedKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CronOccurrence_DefinitionRevision_Status",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "CronTickerId", "DefinitionRevision", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeFinalizationOutbox_AvailableAtUtc_OutboxId",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                columns: new[] { "AvailableAtUtc", "OutboxId" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeFinalizationOutbox_TickerType_TickerId_AcquisitionToken_DispatchId_NodeEpoch",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                columns: new[] { "TickerType", "TickerId", "AcquisitionToken", "DispatchId", "NodeEpoch" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Destructive rollback of the professional TickerQ store upgrade is unsupported. " +
                "Roll back the application binary while leaving the additive schema in place.");

            migrationBuilder.DropTable(
                name: "NodeFinalizationOutbox",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TickerQStoreMetadata",
                schema: "ticker");

            migrationBuilder.DropIndex(
                name: "IX_CronTickers_SeedOwner_Epoch",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "UX_CronTickers_SeedKey",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "IX_CronOccurrence_DefinitionRevision_Status",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "DefinitionRevision",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "RetirementRequestedAt",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "SeedKey",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "SeedLastSeenAt",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "SeedManifestEpoch",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "SeedOwnerNamespace",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "SeedWasEnabledBeforeRetirement",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "DefinitionRevision",
                schema: "ticker",
                table: "CronTickerOccurrences");
        }
    }
}
