using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ServerMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArcanistImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SourceTicket = table.Column<string>(type: "text", nullable: true),
                    ProtectedCode = table.Column<string>(type: "text", nullable: false),
                    Plan = table.Column<string>(type: "text", nullable: false),
                    LibraryMap = table.Column<string>(type: "text", nullable: false),
                    UserMap = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UsersCreated = table.Column<int>(type: "integer", nullable: false),
                    UsersMapped = table.Column<int>(type: "integer", nullable: false),
                    LibrariesCreated = table.Column<int>(type: "integer", nullable: false),
                    SettingsApplied = table.Column<bool>(type: "boolean", nullable: false),
                    ExtensionsInstalled = table.Column<int>(type: "integer", nullable: false),
                    PlayersInstalled = table.Column<int>(type: "integer", nullable: false),
                    FilesTotal = table.Column<int>(type: "integer", nullable: false),
                    FilesCopied = table.Column<int>(type: "integer", nullable: false),
                    FilesPresent = table.Column<int>(type: "integer", nullable: false),
                    FilesFailed = table.Column<int>(type: "integer", nullable: false),
                    CopiedBytes = table.Column<long>(type: "bigint", nullable: false),
                    Ratings = table.Column<int>(type: "integer", nullable: false),
                    PlayCounts = table.Column<int>(type: "integer", nullable: false),
                    Plays = table.Column<int>(type: "integer", nullable: false),
                    Playlists = table.Column<int>(type: "integer", nullable: false),
                    Wishes = table.Column<int>(type: "integer", nullable: false),
                    Unmatched = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcanistImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedBy = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArcanistImportFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcanistImportFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArcanistImportFiles_ArcanistImports_ImportId",
                        column: x => x.ImportId,
                        principalTable: "ArcanistImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArcanistImportFiles_ImportId_Status",
                table: "ArcanistImportFiles",
                columns: new[] { "ImportId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ArcanistImports_Status",
                table: "ArcanistImports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationGrants_CodeHash",
                table: "MigrationGrants",
                column: "CodeHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArcanistImportFiles");

            migrationBuilder.DropTable(
                name: "MigrationGrants");

            migrationBuilder.DropTable(
                name: "ArcanistImports");
        }
    }
}
