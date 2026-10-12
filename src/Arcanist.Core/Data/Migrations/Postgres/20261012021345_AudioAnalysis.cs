using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Postgres
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
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TruePeak",
                table: "Albums",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AudioAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnalyzerVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AnalyzedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Loudness = table.Column<double>(type: "double precision", nullable: true),
                    LoudnessRange = table.Column<double>(type: "double precision", nullable: true),
                    TruePeak = table.Column<double>(type: "double precision", nullable: true),
                    LoudnessHistogram = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Bpm = table.Column<float>(type: "real", nullable: true),
                    BpmConfidence = table.Column<float>(type: "real", nullable: true),
                    KeyTonic = table.Column<int>(type: "integer", nullable: true),
                    KeyMinor = table.Column<bool>(type: "boolean", nullable: true),
                    KeyConfidence = table.Column<float>(type: "real", nullable: true),
                    AcoustIdFingerprint = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Fingerprint = table.Column<byte[]>(type: "bytea", nullable: true),
                    FingerprintDurationMs = table.Column<int>(type: "integer", nullable: true),
                    Waveform = table.Column<byte[]>(type: "bytea", nullable: true)
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
