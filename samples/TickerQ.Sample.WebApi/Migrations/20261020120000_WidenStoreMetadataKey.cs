using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations;

public partial class WidenStoreMetadataKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Id",
            schema: "ticker",
            table: "TickerQStoreMetadata",
            type: "TEXT",
            maxLength: 80,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 64);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException(
            "Runtime activation keys are 78 characters. Leave the widened metadata column in place when rolling back application binaries.");
}
