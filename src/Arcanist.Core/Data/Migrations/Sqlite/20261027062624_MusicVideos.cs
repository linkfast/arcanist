using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MusicVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "VideoObtainedAt",
                table: "WishListItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VideoSeen",
                table: "WishListItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WantsVideo",
                table: "WishListItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MusicVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibrarySourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    NormalizedArtist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SearchText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Container = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VideoCodec = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    AudioCodec = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Probed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LiveLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    ArtworkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LinkMethod = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LinkLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MissingSince = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusicVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MusicVideos_Artworks_ArtworkId",
                        column: x => x.ArtworkId,
                        principalTable: "Artworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MusicVideos_LibrarySources_LibrarySourceId",
                        column: x => x.LibrarySourceId,
                        principalTable: "LibrarySources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoAlignmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Priority = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAlignmentRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoAlignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AudioHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AlignerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Coverage = table.Column<double>(type: "REAL", nullable: false),
                    VideoGainDb = table.Column<double>(type: "REAL", nullable: false),
                    Segments = table.Column<string>(type: "TEXT", nullable: true),
                    AlignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAlignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MusicVideos_ArtworkId",
                table: "MusicVideos",
                column: "ArtworkId");

            migrationBuilder.CreateIndex(
                name: "IX_MusicVideos_ContentHash",
                table: "MusicVideos",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_MusicVideos_LibrarySourceId_RelativePath",
                table: "MusicVideos",
                columns: new[] { "LibrarySourceId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MusicVideos_NormalizedTitle",
                table: "MusicVideos",
                column: "NormalizedTitle");

            migrationBuilder.CreateIndex(
                name: "IX_MusicVideos_RecordingId",
                table: "MusicVideos",
                column: "RecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_MusicVideos_TrackId",
                table: "MusicVideos",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAlignmentRequests_VideoId",
                table: "VideoAlignmentRequests",
                column: "VideoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoAlignments_AudioHash",
                table: "VideoAlignments",
                column: "AudioHash");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAlignments_VideoHash_AudioHash",
                table: "VideoAlignments",
                columns: new[] { "VideoHash", "AudioHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MusicVideos");

            migrationBuilder.DropTable(
                name: "VideoAlignmentRequests");

            migrationBuilder.DropTable(
                name: "VideoAlignments");

            migrationBuilder.DropColumn(
                name: "VideoObtainedAt",
                table: "WishListItems");

            migrationBuilder.DropColumn(
                name: "VideoSeen",
                table: "WishListItems");

            migrationBuilder.DropColumn(
                name: "WantsVideo",
                table: "WishListItems");
        }
    }
}
