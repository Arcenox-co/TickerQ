using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Sample.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddActivationManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivationManifest",
                schema: "ticker",
                table: "TickerQStoreMetadata",
                type: "TEXT",
                maxLength: 48,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActivationManifestHistory",
                schema: "ticker",
                table: "TickerQStoreMetadata",
                type: "TEXT",
                maxLength: 1568,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ActivationEpoch",
                schema: "ticker",
                table: "TickerQNodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivationManifest",
                schema: "ticker",
                table: "TickerQStoreMetadata");

            migrationBuilder.DropColumn(
                name: "ActivationManifestHistory",
                schema: "ticker",
                table: "TickerQStoreMetadata");

            migrationBuilder.DropColumn(
                name: "ActivationEpoch",
                schema: "ticker",
                table: "TickerQNodes");
        }
    }
}
