using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Duplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TrackLocked",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DuplicateCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackAId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackBId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Similarity = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_Status",
                table: "DuplicateCandidates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_TrackAId_TrackBId",
                table: "DuplicateCandidates",
                columns: new[] { "TrackAId", "TrackBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_TrackBId",
                table: "DuplicateCandidates",
                column: "TrackBId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateCandidates");

            migrationBuilder.DropColumn(
                name: "TrackLocked",
                table: "MediaFiles");
        }
    }
}
