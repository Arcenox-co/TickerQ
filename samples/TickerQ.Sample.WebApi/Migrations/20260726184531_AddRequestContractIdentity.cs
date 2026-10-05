using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestContractIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionToken",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseUntil",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OnStale",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestContractFingerprint",
                schema: "ticker",
                table: "TimeTickers",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestContractVersion",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StaleRestartCount",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimeoutSeconds",
                schema: "ticker",
                table: "TimeTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValueSql: "1");


            migrationBuilder.AddColumn<int>(
                name: "OnStale",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestContractFingerprint",
                schema: "ticker",
                table: "CronTickers",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestContractVersion",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeoutSeconds",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionToken",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseUntil",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StaleRestartCount",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionToken",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "OnStale",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "RequestContractFingerprint",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "RequestContractVersion",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "StaleRestartCount",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "TimeoutSeconds",
                schema: "ticker",
                table: "TimeTickers");


            migrationBuilder.DropColumn(
                name: "OnStale",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "RequestContractFingerprint",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "RequestContractVersion",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "TimeoutSeconds",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "AcquisitionToken",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "StaleRestartCount",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                defaultValueSql: "1",
                oldClrType: typeof(bool),
                oldType: "INTEGER");
        }
    }
}
