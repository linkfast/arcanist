using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class PlayEventExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "PlayEvents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "PlayEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_UserId_ExternalId",
                table: "PlayEvents",
                columns: new[] { "UserId", "ExternalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayEvents_UserId_ExternalId",
                table: "PlayEvents");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "PlayEvents");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PlayEvents");
        }
    }
}
