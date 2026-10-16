using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class BackupPeers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupFolder",
                table: "P2PPeers",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BackupQuotaBytes",
                table: "P2PPeers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackupFolder",
                table: "P2PInvitations",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BackupQuotaBytes",
                table: "P2PInvitations",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupFolder",
                table: "P2PPeers");

            migrationBuilder.DropColumn(
                name: "BackupQuotaBytes",
                table: "P2PPeers");

            migrationBuilder.DropColumn(
                name: "BackupFolder",
                table: "P2PInvitations");

            migrationBuilder.DropColumn(
                name: "BackupQuotaBytes",
                table: "P2PInvitations");
        }
    }
}
