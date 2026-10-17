using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ImportSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestionState",
                table: "ImportItems",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suggestions",
                table: "ImportItems",
                type: "TEXT",
                maxLength: 16000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestionsFor",
                table: "ImportItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestionState",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "Suggestions",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "SuggestionsFor",
                table: "ImportItems");
        }
    }
}
