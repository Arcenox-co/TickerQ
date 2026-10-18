using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.Console.Migrations;

public partial class RenameIntegrationSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_NotificationSettings", schema: "ticker", table: "NotificationSettings");
        migrationBuilder.RenameTable(name: "NotificationSettings", schema: "ticker", newName: "IntegrationSettings", newSchema: "ticker");
        migrationBuilder.AddPrimaryKey(name: "PK_IntegrationSettings", schema: "ticker", table: "IntegrationSettings", column: "ApplicationNamespaceKey");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_IntegrationSettings", schema: "ticker", table: "IntegrationSettings");
        migrationBuilder.RenameTable(name: "IntegrationSettings", schema: "ticker", newName: "NotificationSettings", newSchema: "ticker");
        migrationBuilder.AddPrimaryKey(name: "PK_NotificationSettings", schema: "ticker", table: "NotificationSettings", column: "ApplicationNamespaceKey");
    }
}
