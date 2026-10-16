using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Backups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupDestinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Settings = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupDestinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Folder = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeIds = table.Column<string>(type: "text", nullable: false),
                    IncludeUserData = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeServer = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectedPassword = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Schedule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IntervalHours = table.Column<int>(type: "integer", nullable: true),
                    TimeOfDay = table.Column<int>(type: "integer", nullable: true),
                    DayOfWeek = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Backups_BackupDestinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "BackupDestinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Files = table.Column<int>(type: "integer", nullable: false),
                    Copied = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    Versioned = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRuns_Backups_BackupId",
                        column: x => x.BackupId,
                        principalTable: "Backups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupRunFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRunFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRunFailures_BackupRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "BackupRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRunFailures_RunId",
                table: "BackupRunFailures",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_BackupId_RequestedAt",
                table: "BackupRuns",
                columns: new[] { "BackupId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_Status",
                table: "BackupRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_DestinationId",
                table: "Backups",
                column: "DestinationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRunFailures");

            migrationBuilder.DropTable(
                name: "BackupRuns");

            migrationBuilder.DropTable(
                name: "Backups");

            migrationBuilder.DropTable(
                name: "BackupDestinations");
        }
    }
}
