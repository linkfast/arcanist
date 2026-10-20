using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
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
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_CountPlays",
                table: "P2PRemotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SharePlays",
                table: "P2PRemotes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_AllowCache",
                table: "P2PPeers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_CountPlays",
                table: "P2PPeers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_AllowCache",
                table: "P2PInvitations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rules_CountPlays",
                table: "P2PInvitations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RemotePlayEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteUser = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlayedMs = table.Column<int>(type: "integer", nullable: false),
                    Counted = table.Column<bool>(type: "boolean", nullable: false),
                    Skipped = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
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
