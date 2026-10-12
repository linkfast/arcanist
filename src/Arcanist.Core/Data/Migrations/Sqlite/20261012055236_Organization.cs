using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Organization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoOrganize",
                table: "Libraries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoOrganizeSince",
                table: "Libraries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizeTemplate",
                table: "Libraries",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DropFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    LibrarySourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DuplicatePolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConvertTo = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ConvertBitRate = table.Column<int>(type: "INTEGER", nullable: true),
                    ConvertLossy = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DropFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DropFolders_LibrarySources_LibrarySourceId",
                        column: x => x.LibrarySourceId,
                        principalTable: "LibrarySources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizeRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UndoneAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    Moved = table.Column<int>(type: "INTEGER", nullable: false),
                    Failed = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizeRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizeRuns_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DropFolderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Request = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    DestinationSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DestinationPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportItems_DropFolders_DropFolderId",
                        column: x => x.DropFolderId,
                        principalTable: "DropFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizeMoves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    LibrarySourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ToPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Undone = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizeMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizeMoves_OrganizeRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "OrganizeRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DropFolders_LibrarySourceId",
                table: "DropFolders",
                column: "LibrarySourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportItems_ContentHash",
                table: "ImportItems",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_ImportItems_DropFolderId_RelativePath",
                table: "ImportItems",
                columns: new[] { "DropFolderId", "RelativePath" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportItems_Status_UpdatedAt",
                table: "ImportItems",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizeMoves_RunId_Sequence",
                table: "OrganizeMoves",
                columns: new[] { "RunId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizeRuns_LibraryId_StartedAt",
                table: "OrganizeRuns",
                columns: new[] { "LibraryId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportItems");

            migrationBuilder.DropTable(
                name: "OrganizeMoves");

            migrationBuilder.DropTable(
                name: "DropFolders");

            migrationBuilder.DropTable(
                name: "OrganizeRuns");

            migrationBuilder.DropColumn(
                name: "AutoOrganize",
                table: "Libraries");

            migrationBuilder.DropColumn(
                name: "AutoOrganizeSince",
                table: "Libraries");

            migrationBuilder.DropColumn(
                name: "OrganizeTemplate",
                table: "Libraries");
        }
    }
}
