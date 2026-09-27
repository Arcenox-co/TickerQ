using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.ApplicationDbContext.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationRuntimePartitioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CronTickerOccurrenceResults_CronTickerOccurrences_TickerId",
                schema: "ticker",
                table: "CronTickerOccurrenceResults");

            migrationBuilder.DropForeignKey(
                name: "FK_CronTickerOccurrences_CronTickers_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeTickerResults_TimeTickers_TickerId",
                schema: "ticker",
                table: "TimeTickerResults");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeTickers_TimeTickers_ParentId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TimeTickers",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_ChainRootId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_Status_ExecutedAt",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTickers_ParentId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TimeTickerResults",
                schema: "ticker",
                table: "TimeTickerResults");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TickerQStoreMetadata",
                schema: "ticker",
                table: "TickerQStoreMetadata");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NodeFinalizationOutbox",
                schema: "ticker",
                table: "NodeFinalizationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NodeFinalizationOutbox_AvailableAtUtc_OutboxId",
                schema: "ticker",
                table: "NodeFinalizationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NodeFinalizationOutbox_TickerType_TickerId_AcquisitionToken_DispatchId_NodeEpoch",
                schema: "ticker",
                table: "NodeFinalizationOutbox");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CronTickers",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "IX_CronTickers_Expression",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "IX_CronTickers_SeedOwner_Epoch",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "IX_Function_Expression",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "UX_CronTickers_SeedKey",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CronTickerOccurrences",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronOccurrence_DefinitionRevision_Status",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutedAt",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "UQ_CronTickerId_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CronTickerOccurrenceResults",
                schema: "ticker",
                table: "CronTickerOccurrenceResults");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "tq:runtime:v1:5faeafcdf984e28a5c2d2dd032642065c126da7e5ea130c44a0335e5100c2e13");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "TimeTickerResults",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "tq:runtime:v1:5faeafcdf984e28a5c2d2dd032642065c126da7e5ea130c44a0335e5100c2e13");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "TickerQStoreMetadata",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "tq:runtime:v1:5faeafcdf984e28a5c2d2dd032642065c126da7e5ea130c44a0335e5100c2e13");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "tq:runtime:v1:5faeafcdf984e28a5c2d2dd032642065c126da7e5ea130c44a0335e5100c2e13");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "tq:runtime:v1:5faeafcdf984e28a5c2d2dd032642065c126da7e5ea130c44a0335e5100c2e13");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "tq:runtime:v1:5faeafcdf984e28a5c2d2dd032642065c126da7e5ea130c44a0335e5100c2e13");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "CronTickerOccurrenceResults",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "tq:runtime:v1:5faeafcdf984e28a5c2d2dd032642065c126da7e5ea130c44a0335e5100c2e13");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TimeTickers",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_TimeTickerResults",
                schema: "ticker",
                table: "TimeTickerResults",
                columns: new[] { "ApplicationNamespaceKey", "TickerId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_TickerQStoreMetadata",
                schema: "ticker",
                table: "TickerQStoreMetadata",
                columns: new[] { "ApplicationNamespaceKey", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_NodeFinalizationOutbox",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                columns: new[] { "ApplicationNamespaceKey", "OutboxId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_CronTickers",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_CronTickerOccurrences",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_CronTickerOccurrenceResults",
                schema: "ticker",
                table: "CronTickerOccurrenceResults",
                columns: new[] { "ApplicationNamespaceKey", "TickerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ChainRootId",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "ChainRootId" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutedAt",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTickers_ApplicationNamespaceKey_ParentId",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeFinalizationOutbox_ApplicationNamespaceKey_AvailableAtUtc_OutboxId",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                columns: new[] { "ApplicationNamespaceKey", "AvailableAtUtc", "OutboxId" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeFinalizationOutbox_ApplicationNamespaceKey_TickerType_TickerId_AcquisitionToken_DispatchId_NodeEpoch",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                columns: new[] { "ApplicationNamespaceKey", "TickerType", "TickerId", "AcquisitionToken", "DispatchId", "NodeEpoch" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_Expression",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "Expression" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_SeedOwner_Epoch",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "SeedOwnerNamespace", "SeedManifestEpoch" });

            migrationBuilder.CreateIndex(
                name: "IX_Function_Expression",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "Function", "Expression" });

            migrationBuilder.CreateIndex(
                name: "UX_CronTickers_SeedKey",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "SeedKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CronOccurrence_DefinitionRevision_Status",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "CronTickerId", "DefinitionRevision", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "CronTickerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutedAt",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "UQ_CronTickerId_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "CronTickerId", "ExecutionTime" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CronTickerOccurrenceResults_CronTickerOccurrences_ApplicationNamespaceKey_TickerId",
                schema: "ticker",
                table: "CronTickerOccurrenceResults",
                columns: new[] { "ApplicationNamespaceKey", "TickerId" },
                principalSchema: "ticker",
                principalTable: "CronTickerOccurrences",
                principalColumns: new[] { "ApplicationNamespaceKey", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CronTickerOccurrences_CronTickers_ApplicationNamespaceKey_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "CronTickerId" },
                principalSchema: "ticker",
                principalTable: "CronTickers",
                principalColumns: new[] { "ApplicationNamespaceKey", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeTickerResults_TimeTickers_ApplicationNamespaceKey_TickerId",
                schema: "ticker",
                table: "TimeTickerResults",
                columns: new[] { "ApplicationNamespaceKey", "TickerId" },
                principalSchema: "ticker",
                principalTable: "TimeTickers",
                principalColumns: new[] { "ApplicationNamespaceKey", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeTickers_TimeTickers_ApplicationNamespaceKey_ParentId",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "ParentId" },
                principalSchema: "ticker",
                principalTable: "TimeTickers",
                principalColumns: new[] { "ApplicationNamespaceKey", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CronTickerOccurrenceResults_CronTickerOccurrences_ApplicationNamespaceKey_TickerId",
                schema: "ticker",
                table: "CronTickerOccurrenceResults");

            migrationBuilder.DropForeignKey(
                name: "FK_CronTickerOccurrences_CronTickers_ApplicationNamespaceKey_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeTickerResults_TimeTickers_ApplicationNamespaceKey_TickerId",
                schema: "ticker",
                table: "TimeTickerResults");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeTickers_TimeTickers_ApplicationNamespaceKey_ParentId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TimeTickers",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_ChainRootId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_Status_ExecutedAt",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTickers_ApplicationNamespaceKey_ParentId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TimeTickerResults",
                schema: "ticker",
                table: "TimeTickerResults");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TickerQStoreMetadata",
                schema: "ticker",
                table: "TickerQStoreMetadata");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NodeFinalizationOutbox",
                schema: "ticker",
                table: "NodeFinalizationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NodeFinalizationOutbox_ApplicationNamespaceKey_AvailableAtUtc_OutboxId",
                schema: "ticker",
                table: "NodeFinalizationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NodeFinalizationOutbox_ApplicationNamespaceKey_TickerType_TickerId_AcquisitionToken_DispatchId_NodeEpoch",
                schema: "ticker",
                table: "NodeFinalizationOutbox");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CronTickers",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "IX_CronTickers_Expression",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "IX_CronTickers_SeedOwner_Epoch",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "IX_Function_Expression",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropIndex(
                name: "UX_CronTickers_SeedKey",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CronTickerOccurrences",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronOccurrence_DefinitionRevision_Status",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutedAt",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropIndex(
                name: "UQ_CronTickerId_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CronTickerOccurrenceResults",
                schema: "ticker",
                table: "CronTickerOccurrenceResults");

            migrationBuilder.DropColumn(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "TimeTickerResults");

            migrationBuilder.DropColumn(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "TickerQStoreMetadata");

            migrationBuilder.DropColumn(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "NodeFinalizationOutbox");

            migrationBuilder.DropColumn(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "ApplicationNamespaceKey",
                schema: "ticker",
                table: "CronTickerOccurrenceResults");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TimeTickers",
                schema: "ticker",
                table: "TimeTickers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TimeTickerResults",
                schema: "ticker",
                table: "TimeTickerResults",
                column: "TickerId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TickerQStoreMetadata",
                schema: "ticker",
                table: "TickerQStoreMetadata",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NodeFinalizationOutbox",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                column: "OutboxId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CronTickers",
                schema: "ticker",
                table: "CronTickers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CronTickerOccurrences",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CronTickerOccurrenceResults",
                schema: "ticker",
                table: "CronTickerOccurrenceResults",
                column: "TickerId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ChainRootId",
                schema: "ticker",
                table: "TimeTickers",
                column: "ChainRootId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                column: "ExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutedAt",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "Status", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTickers_ParentId",
                schema: "ticker",
                table: "TimeTickers",
                column: "ParentId");

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

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_Expression",
                schema: "ticker",
                table: "CronTickers",
                column: "Expression");

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_SeedOwner_Epoch",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "SeedOwnerNamespace", "SeedManifestEpoch" });

            migrationBuilder.CreateIndex(
                name: "IX_Function_Expression",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "Function", "Expression" });

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
                name: "IX_CronTickerOccurrence_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "CronTickerId");

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "ExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutedAt",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "Status", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "UQ_CronTickerId_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "CronTickerId", "ExecutionTime" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CronTickerOccurrenceResults_CronTickerOccurrences_TickerId",
                schema: "ticker",
                table: "CronTickerOccurrenceResults",
                column: "TickerId",
                principalSchema: "ticker",
                principalTable: "CronTickerOccurrences",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CronTickerOccurrences_CronTickers_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "CronTickerId",
                principalSchema: "ticker",
                principalTable: "CronTickers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeTickerResults_TimeTickers_TickerId",
                schema: "ticker",
                table: "TimeTickerResults",
                column: "TickerId",
                principalSchema: "ticker",
                principalTable: "TimeTickers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeTickers_TimeTickers_ParentId",
                schema: "ticker",
                table: "TimeTickers",
                column: "ParentId",
                principalSchema: "ticker",
                principalTable: "TimeTickers",
                principalColumn: "Id");
        }
    }
}
