using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class JamSharedSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Suggested",
                table: "PlaylistEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Suggestions",
                table: "JamSessions",
                type: "boolean",
                nullable: false,
                // Jams already running get suggestions too, as new ones do.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Suggested",
                table: "PlaylistEntries");

            migrationBuilder.DropColumn(
                name: "Suggestions",
                table: "JamSessions");
        }
    }
}
