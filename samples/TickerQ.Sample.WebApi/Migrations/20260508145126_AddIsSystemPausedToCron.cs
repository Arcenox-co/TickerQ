using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSystemPausedToCron : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValueSql: "1");

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemPaused",
                schema: "ticker",
                table: "CronTickers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSystemPaused",
                schema: "ticker",
                table: "CronTickers");

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
