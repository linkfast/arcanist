using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class VideoLyricsTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoLyricsTimings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlainHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Lrc = table.Column<string>(type: "text", nullable: false),
                    AlignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoLyricsTimings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoLyricsTimings_VideoHash",
                table: "VideoLyricsTimings",
                column: "VideoHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoLyricsTimings");
        }
    }
}
