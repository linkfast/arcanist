using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class CueSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_LibrarySourceId_RelativePath",
                table: "MediaFiles");

            migrationBuilder.AddColumn<int>(
                name: "CueTrack",
                table: "MediaFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EndMs",
                table: "MediaFiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartMs",
                table: "MediaFiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_LibrarySourceId_RelativePath_CueTrack",
                table: "MediaFiles",
                columns: new[] { "LibrarySourceId", "RelativePath", "CueTrack" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_LibrarySourceId_RelativePath_CueTrack",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "CueTrack",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "EndMs",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "StartMs",
                table: "MediaFiles");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_LibrarySourceId_RelativePath",
                table: "MediaFiles",
                columns: new[] { "LibrarySourceId", "RelativePath" },
                unique: true);
        }
    }
}
