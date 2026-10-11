using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ScanJobErrorCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "ScanJobs");

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "ScanJobs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_Status",
                table: "ScanJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScanJobs_Status",
                table: "ScanJobs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "ScanJobs");

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "ScanJobs",
                type: "TEXT",
                nullable: true);
        }
    }
}
