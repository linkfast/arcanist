using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupFolder = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BackupName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Files = table.Column<bool>(type: "boolean", nullable: false),
                    UserData = table.Column<bool>(type: "boolean", nullable: false),
                    Paths = table.Column<string>(type: "text", nullable: false),
                    TargetSourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserMap = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FilesTotal = table.Column<int>(type: "integer", nullable: false),
                    Restored = table.Column<int>(type: "integer", nullable: false),
                    Identical = table.Column<int>(type: "integer", nullable: false),
                    Renamed = table.Column<int>(type: "integer", nullable: false),
                    FilesFailed = table.Column<int>(type: "integer", nullable: false),
                    Ratings = table.Column<int>(type: "integer", nullable: false),
                    PlayCounts = table.Column<int>(type: "integer", nullable: false),
                    Plays = table.Column<int>(type: "integer", nullable: false),
                    Playlists = table.Column<int>(type: "integer", nullable: false),
                    Unmatched = table.Column<int>(type: "integer", nullable: false),
                    Libraries = table.Column<string>(type: "text", nullable: false),
                    Failures = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
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
