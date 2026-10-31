using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class LyricsDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LyricsDrafts",
                columns: table => new
                {
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Lines = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EditedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LyricsDrafts", x => x.TrackId);
                    table.ForeignKey(
                        name: "FK_LyricsDrafts_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LyricsDrafts");
        }
    }
}
