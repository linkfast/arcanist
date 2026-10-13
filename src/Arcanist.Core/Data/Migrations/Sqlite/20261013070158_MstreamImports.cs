using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MstreamImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MstreamImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VpathMap = table.Column<string>(type: "TEXT", nullable: false),
                    UserMap = table.Column<string>(type: "TEXT", nullable: false),
                    LibrariesCreated = table.Column<int>(type: "INTEGER", nullable: false),
                    UsersCreated = table.Column<int>(type: "INTEGER", nullable: false),
                    Playlists = table.Column<int>(type: "INTEGER", nullable: false),
                    Ratings = table.Column<int>(type: "INTEGER", nullable: false),
                    Plays = table.Column<int>(type: "INTEGER", nullable: false),
                    Unmatched = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MstreamImports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MstreamImports_Status",
                table: "MstreamImports",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MstreamImports");
        }
    }
}
