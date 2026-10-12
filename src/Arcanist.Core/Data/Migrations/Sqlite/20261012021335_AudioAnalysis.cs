using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AudioAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Loudness",
                table: "Albums",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TruePeak",
                table: "Albums",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AudioAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AnalyzerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AnalyzedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Loudness = table.Column<double>(type: "REAL", nullable: true),
                    LoudnessRange = table.Column<double>(type: "REAL", nullable: true),
                    TruePeak = table.Column<double>(type: "REAL", nullable: true),
                    LoudnessHistogram = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Bpm = table.Column<float>(type: "REAL", nullable: true),
                    BpmConfidence = table.Column<float>(type: "REAL", nullable: true),
                    KeyTonic = table.Column<int>(type: "INTEGER", nullable: true),
                    KeyMinor = table.Column<bool>(type: "INTEGER", nullable: true),
                    KeyConfidence = table.Column<float>(type: "REAL", nullable: true),
                    AcoustIdFingerprint = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Fingerprint = table.Column<byte[]>(type: "BLOB", nullable: true),
                    FingerprintDurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Waveform = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioAnalyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioAnalyses_ContentHash",
                table: "AudioAnalyses",
                column: "ContentHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioAnalyses");

            migrationBuilder.DropColumn(
                name: "Loudness",
                table: "Albums");

            migrationBuilder.DropColumn(
                name: "TruePeak",
                table: "Albums");
        }
    }
}
