using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations;

public partial class AddChainDeleteBehavior : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<int>(name: "DeleteBehavior", schema: "ticker", table: "TimeTickers", type: "INTEGER", nullable: false, defaultValue: 0);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "DeleteBehavior", schema: "ticker", table: "TimeTickers");
}
