using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class RemoteLibraries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Rules_AllowCache",
                table: "P2PRemotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_CountPlays",
                table: "P2PRemotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SharePlays",
                table: "P2PRemotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_AllowCache",
                table: "P2PPeers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_CountPlays",
                table: "P2PPeers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_AllowCache",
                table: "P2PInvitations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_CountPlays",
                table: "P2PInvitations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RemotePlayEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemoteUser = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlayedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlayedMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Counted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemotePlayEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemotePlayEvents_P2PPeers_PeerId",
                        column: x => x.PeerId,
                        principalTable: "P2PPeers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RemotePlayEvents_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemotePlayEvents_PeerId_ExternalId",
                table: "RemotePlayEvents",
                columns: new[] { "PeerId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemotePlayEvents_PlayedAt",
                table: "RemotePlayEvents",
                column: "PlayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RemotePlayEvents_TrackId_PlayedAt",
                table: "RemotePlayEvents",
                columns: new[] { "TrackId", "PlayedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemotePlayEvents");

            migrationBuilder.DropColumn(
                name: "Rules_AllowCache",
                table: "P2PRemotes");

            migrationBuilder.DropColumn(
                name: "Rules_CountPlays",
                table: "P2PRemotes");

            migrationBuilder.DropColumn(
                name: "SharePlays",
                table: "P2PRemotes");

            migrationBuilder.DropColumn(
                name: "Rules_AllowCache",
                table: "P2PPeers");

            migrationBuilder.DropColumn(
                name: "Rules_CountPlays",
                table: "P2PPeers");

            migrationBuilder.DropColumn(
                name: "Rules_AllowCache",
                table: "P2PInvitations");

            migrationBuilder.DropColumn(
                name: "Rules_CountPlays",
                table: "P2PInvitations");
        }
    }
}
