using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class LibraryNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Libraries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing libraries are numbered by creation order (the same SQL works in SQLite and PostgreSQL).
            migrationBuilder.Sql("""
                UPDATE "Libraries" SET "Number" = (
                    SELECT COUNT(*) FROM "Libraries" AS "Other"
                    WHERE "Other"."CreatedAt" < "Libraries"."CreatedAt"
                       OR ("Other"."CreatedAt" = "Libraries"."CreatedAt" AND "Other"."Id" <= "Libraries"."Id"))
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_Number",
                table: "Libraries",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Libraries_Number",
                table: "Libraries");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Libraries");
        }
    }
}
