using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Restores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestoreJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DestinationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackupFolder = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BackupName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Snapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Files = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserData = table.Column<bool>(type: "INTEGER", nullable: false),
                    Paths = table.Column<string>(type: "TEXT", nullable: false),
                    TargetSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserMap = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilesTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    Restored = table.Column<int>(type: "INTEGER", nullable: false),
                    Identical = table.Column<int>(type: "INTEGER", nullable: false),
                    Renamed = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesFailed = table.Column<int>(type: "INTEGER", nullable: false),
                    Ratings = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayCounts = table.Column<int>(type: "INTEGER", nullable: false),
                    Plays = table.Column<int>(type: "INTEGER", nullable: false),
                    Playlists = table.Column<int>(type: "INTEGER", nullable: false),
                    Unmatched = table.Column<int>(type: "INTEGER", nullable: false),
                    Libraries = table.Column<string>(type: "TEXT", nullable: false),
                    Failures = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreJobs_BackupDestinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "BackupDestinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestoreJobs_DestinationId",
                table: "RestoreJobs",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreJobs_Status",
                table: "RestoreJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestoreJobs");
        }
    }
}
