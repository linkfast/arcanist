using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class TrackCommentAndBpm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "Tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TaggedBpm",
                table: "Tracks",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "TaggedBpm",
                table: "Tracks");
        }
    }
}
