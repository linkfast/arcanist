using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class MstreamCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CopiedBytes",
                table: "MstreamImports",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "CopyMap",
                table: "MstreamImports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FilesCopied",
                table: "MstreamImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FilesFailed",
                table: "MstreamImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FilesPresent",
                table: "MstreamImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FilesTotal",
                table: "MstreamImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PendingData",
                table: "MstreamImports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Session",
                table: "MstreamImports",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MstreamImportFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vpath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MstreamImportFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MstreamImportFiles_MstreamImports_ImportId",
                        column: x => x.ImportId,
                        principalTable: "MstreamImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MstreamImportFiles_ImportId_Status",
                table: "MstreamImportFiles",
                columns: new[] { "ImportId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MstreamImportFiles");

            migrationBuilder.DropColumn(
                name: "CopiedBytes",
                table: "MstreamImports");

            migrationBuilder.DropColumn(
                name: "CopyMap",
                table: "MstreamImports");

            migrationBuilder.DropColumn(
                name: "FilesCopied",
                table: "MstreamImports");

            migrationBuilder.DropColumn(
                name: "FilesFailed",
                table: "MstreamImports");

            migrationBuilder.DropColumn(
                name: "FilesPresent",
                table: "MstreamImports");

            migrationBuilder.DropColumn(
                name: "FilesTotal",
                table: "MstreamImports");

            migrationBuilder.DropColumn(
                name: "PendingData",
                table: "MstreamImports");

            migrationBuilder.DropColumn(
                name: "Session",
                table: "MstreamImports");
        }
    }
}
