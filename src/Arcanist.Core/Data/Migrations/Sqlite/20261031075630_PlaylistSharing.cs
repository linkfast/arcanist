using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class PlaylistSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Playlists",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<DateTime>(
                name: "LinkCreatedAt",
                table: "Playlists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkToken",
                table: "Playlists",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AddedByDj",
                table: "PlaylistEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "AddedByGuestId",
                table: "PlaylistEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlayedAt",
                table: "PlaylistEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "PlaylistCollaborators",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Edit");

            migrationBuilder.CreateTable(
                name: "JamSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RendererId = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    HostDeviceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HostClient = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    HostSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Voting = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowGuests = table.Column<bool>(type: "INTEGER", nullable: false),
                    GuestLibraryIds = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: true),
                    DjKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DjName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    DjMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DjSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExternalNowPlaying = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    PositionMs = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PinnedEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JamSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JamSessions_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JamGuests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Banned = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JamGuests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JamGuests_JamSessions_JamId",
                        column: x => x.JamId,
                        principalTable: "JamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JamVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GuestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CastAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FromAdding = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JamVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JamVotes_JamSessions_JamId",
                        column: x => x.JamId,
                        principalTable: "JamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_LinkToken",
                table: "Playlists",
                column: "LinkToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JamGuests_JamId",
                table: "JamGuests",
                column: "JamId");

            migrationBuilder.CreateIndex(
                name: "IX_JamGuests_TokenHash",
                table: "JamGuests",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JamSessions_Code",
                table: "JamSessions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JamSessions_HostUserId",
                table: "JamSessions",
                column: "HostUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JamSessions_PlaylistId",
                table: "JamSessions",
                column: "PlaylistId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JamVotes_JamId_EntryId",
                table: "JamVotes",
                columns: new[] { "JamId", "EntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_JamVotes_JamId_GuestId_CastAt",
                table: "JamVotes",
                columns: new[] { "JamId", "GuestId", "CastAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JamVotes_JamId_UserId_CastAt",
                table: "JamVotes",
                columns: new[] { "JamId", "UserId", "CastAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JamGuests");

            migrationBuilder.DropTable(
                name: "JamVotes");

            migrationBuilder.DropTable(
                name: "JamSessions");

            migrationBuilder.DropIndex(
                name: "IX_Playlists_LinkToken",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "LinkCreatedAt",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "LinkToken",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "AddedByDj",
                table: "PlaylistEntries");

            migrationBuilder.DropColumn(
                name: "AddedByGuestId",
                table: "PlaylistEntries");

            migrationBuilder.DropColumn(
                name: "PlayedAt",
                table: "PlaylistEntries");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "PlaylistCollaborators");
        }
    }
}
