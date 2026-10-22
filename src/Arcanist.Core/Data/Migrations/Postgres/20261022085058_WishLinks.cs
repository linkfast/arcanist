using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WishLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArtworkId",
                table: "WishListItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceService",
                table: "WishListItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "WishListItems",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtworkId",
                table: "WishListItems");

            migrationBuilder.DropColumn(
                name: "SourceService",
                table: "WishListItems");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "WishListItems");
        }
    }
}
