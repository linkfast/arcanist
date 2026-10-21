using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ManagedLibraries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManagedBy",
                table: "Libraries",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagedKey",
                table: "Libraries",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_ManagedBy_ManagedKey",
                table: "Libraries",
                columns: new[] { "ManagedBy", "ManagedKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Libraries_ManagedBy_ManagedKey",
                table: "Libraries");

            migrationBuilder.DropColumn(
                name: "ManagedBy",
                table: "Libraries");

            migrationBuilder.DropColumn(
                name: "ManagedKey",
                table: "Libraries");
        }
    }
}
