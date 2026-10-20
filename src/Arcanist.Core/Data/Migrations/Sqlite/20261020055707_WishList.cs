using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WishList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WishListItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Album = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    RecordingMbid = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcoustId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ObtainedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatchedBy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FoundWhenAdded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Seen = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishListItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WishListItems_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WishListItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WishListItems_ObtainedAt",
                table: "WishListItems",
                column: "ObtainedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WishListItems_TrackId",
                table: "WishListItems",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_WishListItems_UserId_CreatedAt",
                table: "WishListItems",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WishListItems");
        }
    }
}
