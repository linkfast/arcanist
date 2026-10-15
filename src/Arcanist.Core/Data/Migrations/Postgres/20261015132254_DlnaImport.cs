using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class DlnaImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DlnaImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Location = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ServerName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Containers = table.Column<string>(type: "text", nullable: false),
                    LibrarySourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CopiedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlnaImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DlnaImports_LibrarySources_LibrarySourceId",
                        column: x => x.LibrarySourceId,
                        principalTable: "LibrarySources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DlnaImportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectId = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Extension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Artist = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AlbumArtist = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Album = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Genre = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    TrackNumber = table.Column<int>(type: "integer", nullable: true),
                    DiscNumber = table.Column<int>(type: "integer", nullable: true),
                    ArtUrl = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DestinationPath = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlnaImportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DlnaImportItems_DlnaImports_ImportId",
                        column: x => x.ImportId,
                        principalTable: "DlnaImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DlnaImportItems_ImportId_Status",
                table: "DlnaImportItems",
                columns: new[] { "ImportId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DlnaImports_LibrarySourceId",
                table: "DlnaImports",
                column: "LibrarySourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DlnaImports_Status",
                table: "DlnaImports",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DlnaImportItems");

            migrationBuilder.DropTable(
                name: "DlnaImports");
        }
    }
}
