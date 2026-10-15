using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class P2PNetwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "P2PActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PeerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeerName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "P2PInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccessExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LibraryIds = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Rules_AllowDownload = table.Column<bool>(type: "boolean", nullable: false),
                    Rules_BandwidthKbps = table.Column<int>(type: "integer", nullable: true),
                    Rules_MaxBitRate = table.Column<int>(type: "integer", nullable: true),
                    Rules_MaxStreams = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "P2PPeers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BytesSent = table.Column<long>(type: "bigint", nullable: false),
                    BytesReceived = table.Column<long>(type: "bigint", nullable: false),
                    ConnectedSeconds = table.Column<long>(type: "bigint", nullable: false),
                    PeakBytesPerSecond = table.Column<long>(type: "bigint", nullable: false),
                    Streams = table.Column<long>(type: "bigint", nullable: false),
                    Rules_AllowDownload = table.Column<bool>(type: "boolean", nullable: false),
                    Rules_BandwidthKbps = table.Column<int>(type: "integer", nullable: true),
                    Rules_MaxBitRate = table.Column<int>(type: "integer", nullable: true),
                    Rules_MaxStreams = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PPeers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_P2PPeers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "P2PRemotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ticket = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastContactAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Rules_AllowDownload = table.Column<bool>(type: "boolean", nullable: false),
                    Rules_BandwidthKbps = table.Column<int>(type: "integer", nullable: true),
                    Rules_MaxBitRate = table.Column<int>(type: "integer", nullable: true),
                    Rules_MaxStreams = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PRemotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_P2PRemotes_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "P2PPeerLibraries",
                columns: table => new
                {
                    PeerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PPeerLibraries", x => new { x.PeerId, x.LibraryId });
                    table.ForeignKey(
                        name: "FK_P2PPeerLibraries_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_P2PPeerLibraries_P2PPeers_PeerId",
                        column: x => x.PeerId,
                        principalTable: "P2PPeers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_P2PActivities_At",
                table: "P2PActivities",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_P2PActivities_PeerId_At",
                table: "P2PActivities",
                columns: new[] { "PeerId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_P2PInvitations_ExpiresAt",
                table: "P2PInvitations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_P2PInvitations_TokenHash",
                table: "P2PInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_P2PPeerLibraries_LibraryId",
                table: "P2PPeerLibraries",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_P2PPeers_NodeId",
                table: "P2PPeers",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_P2PPeers_UserId",
                table: "P2PPeers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_P2PRemotes_LibraryId",
                table: "P2PRemotes",
                column: "LibraryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_P2PRemotes_NodeId",
                table: "P2PRemotes",
                column: "NodeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "P2PActivities");

            migrationBuilder.DropTable(
                name: "P2PInvitations");

            migrationBuilder.DropTable(
                name: "P2PPeerLibraries");

            migrationBuilder.DropTable(
                name: "P2PRemotes");

            migrationBuilder.DropTable(
                name: "P2PPeers");
        }
    }
}
