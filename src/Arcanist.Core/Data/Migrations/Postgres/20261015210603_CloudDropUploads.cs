using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class CloudDropUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UploadToCloud",
                table: "DropFolders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LibraryPath",
                table: "CloudDropFetches",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LibrarySourceId",
                table: "CloudDropFetches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrackId",
                table: "CloudDropFetches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Upload",
                table: "CloudDropFetches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedTrackId",
                table: "CloudDropFetches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadToCloud",
                table: "DropFolders");

            migrationBuilder.DropColumn(
                name: "LibraryPath",
                table: "CloudDropFetches");

            migrationBuilder.DropColumn(
                name: "LibrarySourceId",
                table: "CloudDropFetches");

            migrationBuilder.DropColumn(
                name: "TrackId",
                table: "CloudDropFetches");

            migrationBuilder.DropColumn(
                name: "Upload",
                table: "CloudDropFetches");

            migrationBuilder.DropColumn(
                name: "UploadedTrackId",
                table: "CloudDropFetches");
        }
    }
}
