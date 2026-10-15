using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class LibraryScanMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ScanIntervalHours",
                table: "Libraries",
                newName: "ScanIntervalMinutes");

            // The interval was in hours.
            migrationBuilder.Sql("UPDATE \"Libraries\" SET \"ScanIntervalMinutes\" = \"ScanIntervalMinutes\" * 60 WHERE \"ScanIntervalMinutes\" IS NOT NULL;");

            migrationBuilder.AddColumn<bool>(
                name: "WatchForChanges",
                table: "Libraries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WatchForChanges",
                table: "Libraries");

            migrationBuilder.Sql("UPDATE \"Libraries\" SET \"ScanIntervalMinutes\" = (\"ScanIntervalMinutes\" + 59) / 60 WHERE \"ScanIntervalMinutes\" IS NOT NULL;");

            migrationBuilder.RenameColumn(
                name: "ScanIntervalMinutes",
                table: "Libraries",
                newName: "ScanIntervalHours");
        }
    }
}
