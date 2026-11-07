using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class LyricsLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LyricsReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Readings = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LyricsReadings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LyricsTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    From = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Lines = table.Column<string>(type: "text", nullable: false),
                    Engine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LyricsTranslations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LyricsReadings_TextHash",
                table: "LyricsReadings",
                column: "TextHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LyricsTranslations_TextHash_Language",
                table: "LyricsTranslations",
                columns: new[] { "TextHash", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LyricsReadings");

            migrationBuilder.DropTable(
                name: "LyricsTranslations");
        }
    }
}
