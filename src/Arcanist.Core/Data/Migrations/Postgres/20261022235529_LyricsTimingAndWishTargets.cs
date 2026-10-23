using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class LyricsTimingAndWishTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetLibraryId",
                table: "WishListItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedSource",
                table: "Lyrics",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetLibraryId",
                table: "WishListItems");

            migrationBuilder.DropColumn(
                name: "SyncedSource",
                table: "Lyrics");
        }
    }
}
