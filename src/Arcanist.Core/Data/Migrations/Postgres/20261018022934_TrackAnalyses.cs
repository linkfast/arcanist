using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class TrackAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayEvents_TrackId",
                table: "PlayEvents");

            migrationBuilder.CreateTable(
                name: "TrackAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnalysisVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AnalyzedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    Json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackAnalysisRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackAnalysisRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_TrackId_PlayedAt",
                table: "PlayEvents",
                columns: new[] { "TrackId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalyses_AnalyzedAt",
                table: "TrackAnalyses",
                column: "AnalyzedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalyses_ContentHash",
                table: "TrackAnalyses",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalysisRequests_ContentHash",
                table: "TrackAnalysisRequests",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalysisRequests_Priority_RequestedAt",
                table: "TrackAnalysisRequests",
                columns: new[] { "Priority", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackAnalyses");

            migrationBuilder.DropTable(
                name: "TrackAnalysisRequests");

            migrationBuilder.DropIndex(
                name: "IX_PlayEvents_TrackId_PlayedAt",
                table: "PlayEvents");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_TrackId",
                table: "PlayEvents",
                column: "TrackId");
        }
    }
}
