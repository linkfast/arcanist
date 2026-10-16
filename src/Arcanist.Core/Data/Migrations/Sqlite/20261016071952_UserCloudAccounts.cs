using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class UserCloudAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountUserId",
                table: "LibraryCloudSyncs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Libraries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_OwnerUserId",
                table: "Libraries",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Libraries_Users_OwnerUserId",
                table: "Libraries",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Libraries_Users_OwnerUserId",
                table: "Libraries");

            migrationBuilder.DropIndex(
                name: "IX_Libraries_OwnerUserId",
                table: "Libraries");

            migrationBuilder.DropColumn(
                name: "AccountUserId",
                table: "LibraryCloudSyncs");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Libraries");
        }
    }
}
