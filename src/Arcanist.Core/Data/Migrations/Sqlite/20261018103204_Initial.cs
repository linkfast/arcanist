using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcanist.Core.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SortName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    MusicBrainzId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SearchText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Artworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artworks", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "BackupDestinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupDestinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DuplicateCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackAId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackBId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Similarity = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateCandidates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

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
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Session = table.Column<string>(type: "TEXT", nullable: true),
                    PendingData = table.Column<string>(type: "TEXT", nullable: true),
                    CopyMap = table.Column<string>(type: "TEXT", nullable: true),
                    FilesTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesCopied = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesPresent = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesFailed = table.Column<int>(type: "INTEGER", nullable: false),
                    CopiedBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MstreamImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnlineLookups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Service = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RetryAfter = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineLookups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "P2PActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PeerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PeerName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "P2PInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PeerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccessExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LibraryIds = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    BackupFolder = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    BackupQuotaBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Rules_AllowDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rules_BandwidthKbps = table.Column<int>(type: "INTEGER", nullable: true),
                    Rules_MaxBitRate = table.Column<int>(type: "INTEGER", nullable: true),
                    Rules_MaxStreams = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recordings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MusicBrainzId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcoustId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TrackAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AnalysisVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AnalyzedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackAnalysisRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackAnalysisRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Permissions = table.Column<long>(type: "INTEGER", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AlbumArtistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    IsCompilation = table.Column<bool>(type: "INTEGER", nullable: false),
                    MusicBrainzReleaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArtworkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Loudness = table.Column<double>(type: "REAL", nullable: true),
                    TruePeak = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albums_Artists_AlbumArtistId",
                        column: x => x.AlbumArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Albums_Artworks_ArtworkId",
                        column: x => x.ArtworkId,
                        principalTable: "Artworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DestinationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Folder = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ScopeIds = table.Column<string>(type: "TEXT", nullable: false),
                    IncludeUserData = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeServer = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProtectedPassword = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Schedule = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntervalHours = table.Column<int>(type: "INTEGER", nullable: true),
                    TimeOfDay = table.Column<int>(type: "INTEGER", nullable: true),
                    DayOfWeek = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Backups_BackupDestinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "BackupDestinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RestoreJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DestinationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackupFolder = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BackupName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Snapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Files = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserData = table.Column<bool>(type: "INTEGER", nullable: false),
                    Paths = table.Column<string>(type: "TEXT", nullable: false),
                    TargetSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserMap = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilesTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    Restored = table.Column<int>(type: "INTEGER", nullable: false),
                    Identical = table.Column<int>(type: "INTEGER", nullable: false),
                    Renamed = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesFailed = table.Column<int>(type: "INTEGER", nullable: false),
                    Ratings = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayCounts = table.Column<int>(type: "INTEGER", nullable: false),
                    Plays = table.Column<int>(type: "INTEGER", nullable: false),
                    Playlists = table.Column<int>(type: "INTEGER", nullable: false),
                    Unmatched = table.Column<int>(type: "INTEGER", nullable: false),
                    Libraries = table.Column<string>(type: "TEXT", nullable: false),
                    Failures = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreJobs_BackupDestinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "BackupDestinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MstreamImportFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Vpath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "AppPasswords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EncryptedSecret = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPasswords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPasswords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Libraries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    VisibleToAll = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScanIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    WatchForChanges = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScanAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OrganizeTemplate = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    AutoOrganize = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoOrganizeSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Libraries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Libraries_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "P2PPeers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BytesSent = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesReceived = table.Column<long>(type: "INTEGER", nullable: false),
                    ConnectedSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    PeakBytesPerSecond = table.Column<long>(type: "INTEGER", nullable: false),
                    Streams = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupFolder = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    BackupQuotaBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Rules_AllowDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rules_BandwidthKbps = table.Column<int>(type: "INTEGER", nullable: true),
                    Rules_MaxBitRate = table.Column<int>(type: "INTEGER", nullable: true),
                    Rules_MaxStreams = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PPeers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_P2PPeers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SearchText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Playlists_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AuthenticatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AlbumId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ArtworkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tracks_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tracks_Artworks_ArtworkId",
                        column: x => x.ArtworkId,
                        principalTable: "Artworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tracks_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BackupRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Snapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Files = table.Column<int>(type: "INTEGER", nullable: false),
                    Copied = table.Column<int>(type: "INTEGER", nullable: false),
                    Unchanged = table.Column<int>(type: "INTEGER", nullable: false),
                    Versioned = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Failed = table.Column<int>(type: "INTEGER", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRuns_Backups_BackupId",
                        column: x => x.BackupId,
                        principalTable: "Backups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudSyncItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AudioHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CloudTrackId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MatchKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudSyncItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudSyncItems_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryAccess",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryAccess", x => new { x.UserId, x.LibraryId });
                    table.ForeignKey(
                        name: "FK_LibraryAccess_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryAccess_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryCloudSyncs",
                columns: table => new
                {
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DownloadSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccountUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryCloudSyncs", x => x.LibraryId);
                    table.ForeignKey(
                        name: "FK_LibraryCloudSyncs_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibrarySources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibrarySources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibrarySources_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
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
                name: "P2PRemotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Ticket = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastContactAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Rules_AllowDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rules_BandwidthKbps = table.Column<int>(type: "INTEGER", nullable: true),
                    Rules_MaxBitRate = table.Column<int>(type: "INTEGER", nullable: true),
                    Rules_MaxStreams = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PRemotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_P2PRemotes_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScanJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilesSeen = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesUpdated = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesMissing = table.Column<int>(type: "INTEGER", nullable: false),
                    Errors = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanJobs_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "P2PPeerLibraries",
                columns: table => new
                {
                    PeerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P2PPeerLibraries", x => new { x.PeerId, x.LibraryId });
                    table.ForeignKey(
                        name: "FK_P2PPeerLibraries_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_P2PPeerLibraries_P2PPeers_PeerId",
                        column: x => x.PeerId,
                        principalTable: "P2PPeers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistCollaborators",
                columns: table => new
                {
                    PlaylistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistCollaborators", x => new { x.PlaylistId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PlaylistCollaborators_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistCollaborators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lyrics",
                columns: table => new
                {
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Plain = table.Column<string>(type: "TEXT", nullable: true),
                    Synced = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Instrumental = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lyrics", x => x.TrackId);
                    table.ForeignKey(
                        name: "FK_Lyrics_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlayedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlayedMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Counted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<bool>(type: "INTEGER", nullable: false),
                    Client = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayEvents_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistEntries_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistEntries_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackArtists",
                columns: table => new
                {
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackArtists", x => new { x.TrackId, x.ArtistId, x.Role });
                    table.ForeignKey(
                        name: "FK_TrackArtists_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackArtists_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackGenres",
                columns: table => new
                {
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenreId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackGenres", x => new { x.TrackId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_TrackGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackGenres_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackPlayStats",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkipCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackPlayStats", x => new { x.UserId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_TrackPlayStats_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackPlayStats_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackRatings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    RatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackRatings", x => new { x.UserId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_TrackRatings_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackRatings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CloudTrackId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserUploads_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserUploads_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserUploads_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupRunFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRunFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRunFailures_BackupRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "BackupRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DlnaImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Containers = table.Column<string>(type: "TEXT", nullable: false),
                    LibrarySourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CopiedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
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
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadToCloud = table.Column<bool>(type: "INTEGER", nullable: false),
                    UploadAccount = table.Column<string>(type: "TEXT", nullable: true),
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
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibrarySourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    CueTrack = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMs = table.Column<int>(type: "INTEGER", nullable: true),
                    EndMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BitRate = table.Column<int>(type: "INTEGER", nullable: true),
                    SampleRate = table.Column<int>(type: "INTEGER", nullable: true),
                    BitDepth = table.Column<int>(type: "INTEGER", nullable: true),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplayGainTrackGain = table.Column<double>(type: "REAL", nullable: true),
                    ReplayGainTrackPeak = table.Column<double>(type: "REAL", nullable: true),
                    ReplayGainAlbumGain = table.Column<double>(type: "REAL", nullable: true),
                    ReplayGainAlbumPeak = table.Column<double>(type: "REAL", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MissingSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TrackLocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaFiles_LibrarySources_LibrarySourceId",
                        column: x => x.LibrarySourceId,
                        principalTable: "LibrarySources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaFiles_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "DlnaImportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObjectId = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AlbumArtist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Album = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Genre = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ArtUrl = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DestinationPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "CloudDropFetches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DropFolderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CloudTrackId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    LibrarySourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LibraryPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Upload = table.Column<int>(type: "INTEGER", nullable: true),
                    UploadedTrackId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudDropFetches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudDropFetches_DropFolders_DropFolderId",
                        column: x => x.DropFolderId,
                        principalTable: "DropFolders",
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
                    SuggestionState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SuggestionsFor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Suggestions = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
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

            migrationBuilder.CreateIndex(
                name: "IX_Albums_AlbumArtistId_NormalizedTitle",
                table: "Albums",
                columns: new[] { "AlbumArtistId", "NormalizedTitle" });

            migrationBuilder.CreateIndex(
                name: "IX_Albums_ArtworkId",
                table: "Albums",
                column: "ArtworkId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_MusicBrainzReleaseId",
                table: "Albums",
                column: "MusicBrainzReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPasswords_SecretHash",
                table: "AppPasswords",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPasswords_UserId",
                table: "AppPasswords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_MusicBrainzId",
                table: "Artists",
                column: "MusicBrainzId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_NormalizedName",
                table: "Artists",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Artworks_Hash",
                table: "Artworks",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AudioAnalyses_ContentHash",
                table: "AudioAnalyses",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupRunFailures_RunId",
                table: "BackupRunFailures",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_BackupId_RequestedAt",
                table: "BackupRuns",
                columns: new[] { "BackupId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_Status",
                table: "BackupRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_DestinationId",
                table: "Backups",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudDropFetches_DropFolderId_CloudTrackId",
                table: "CloudDropFetches",
                columns: new[] { "DropFolderId", "CloudTrackId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CloudSyncItems_LibraryId_CloudTrackId",
                table: "CloudSyncItems",
                columns: new[] { "LibraryId", "CloudTrackId" });

            migrationBuilder.CreateIndex(
                name: "IX_CloudSyncItems_LibraryId_ContentHash",
                table: "CloudSyncItems",
                columns: new[] { "LibraryId", "ContentHash" });

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

            migrationBuilder.CreateIndex(
                name: "IX_DropFolders_LibrarySourceId",
                table: "DropFolders",
                column: "LibrarySourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_Status",
                table: "DuplicateCandidates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_TrackAId_TrackBId",
                table: "DuplicateCandidates",
                columns: new[] { "TrackAId", "TrackBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_TrackBId",
                table: "DuplicateCandidates",
                column: "TrackBId");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_NormalizedName",
                table: "Genres",
                column: "NormalizedName",
                unique: true);

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
                name: "IX_Libraries_Number",
                table: "Libraries",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_OwnerUserId",
                table: "Libraries",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryAccess_LibraryId",
                table: "LibraryAccess",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySources_LibraryId",
                table: "LibrarySources",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_ContentHash",
                table: "MediaFiles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_LibrarySourceId_RelativePath_CueTrack",
                table: "MediaFiles",
                columns: new[] { "LibrarySourceId", "RelativePath", "CueTrack" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_TrackId",
                table: "MediaFiles",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_MstreamImportFiles_ImportId_Status",
                table: "MstreamImportFiles",
                columns: new[] { "ImportId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MstreamImports_Status",
                table: "MstreamImports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineLookups_Service_Subject",
                table: "OnlineLookups",
                columns: new[] { "Service", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizeMoves_RunId_Sequence",
                table: "OrganizeMoves",
                columns: new[] { "RunId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizeRuns_LibraryId_StartedAt",
                table: "OrganizeRuns",
                columns: new[] { "LibraryId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_P2PActivities_At",
                table: "P2PActivities",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_P2PActivities_PeerId_At",
                table: "P2PActivities",
                columns: new[] { "PeerId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_P2PInvitations_ExpiresAt",
                table: "P2PInvitations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_P2PInvitations_TokenHash",
                table: "P2PInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_P2PPeerLibraries_LibraryId",
                table: "P2PPeerLibraries",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_P2PPeers_NodeId",
                table: "P2PPeers",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_P2PPeers_UserId",
                table: "P2PPeers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_P2PRemotes_LibraryId",
                table: "P2PRemotes",
                column: "LibraryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_P2PRemotes_NodeId",
                table: "P2PRemotes",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_PlayedAt",
                table: "PlayEvents",
                column: "PlayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_TrackId_PlayedAt",
                table: "PlayEvents",
                columns: new[] { "TrackId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_UserId_ExternalId",
                table: "PlayEvents",
                columns: new[] { "UserId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_UserId_PlayedAt",
                table: "PlayEvents",
                columns: new[] { "UserId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistCollaborators_UserId",
                table: "PlaylistCollaborators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_PlaylistId_Position",
                table: "PlaylistEntries",
                columns: new[] { "PlaylistId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_TrackId",
                table: "PlaylistEntries",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_NormalizedName",
                table: "Playlists",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_OwnerId",
                table: "Playlists",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_AcoustId",
                table: "Recordings",
                column: "AcoustId");

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_MusicBrainzId",
                table: "Recordings",
                column: "MusicBrainzId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestoreJobs_DestinationId",
                table: "RestoreJobs",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreJobs_Status",
                table: "RestoreJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_LibraryId_RequestedAt",
                table: "ScanJobs",
                columns: new[] { "LibraryId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_Status",
                table: "ScanJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalyses_AnalyzedAt",
                table: "TrackAnalyses",
                column: "AnalyzedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalyses_ContentHash",
                table: "TrackAnalyses",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalysisRequests_ContentHash",
                table: "TrackAnalysisRequests",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackAnalysisRequests_Priority_RequestedAt",
                table: "TrackAnalysisRequests",
                columns: new[] { "Priority", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackArtists_ArtistId",
                table: "TrackArtists",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackGenres_GenreId",
                table: "TrackGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackPlayStats_TrackId",
                table: "TrackPlayStats",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackPlayStats_UserId_LastPlayedAt",
                table: "TrackPlayStats",
                columns: new[] { "UserId", "LastPlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackRatings_TrackId",
                table: "TrackRatings",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackRatings_UserId_Rating_RatedAt",
                table: "TrackRatings",
                columns: new[] { "UserId", "Rating", "RatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_AlbumId_DiscNumber_TrackNumber",
                table: "Tracks",
                columns: new[] { "AlbumId", "DiscNumber", "TrackNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_ArtworkId",
                table: "Tracks",
                column: "ArtworkId");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_NormalizedTitle",
                table: "Tracks",
                column: "NormalizedTitle");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_RecordingId",
                table: "Tracks",
                column: "RecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedUserName",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_RefreshTokenHash",
                table: "UserSessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserUploads_Kind_Status",
                table: "UserUploads",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserUploads_LibraryId",
                table: "UserUploads",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserUploads_TrackId",
                table: "UserUploads",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_UserUploads_UserId_CreatedAt",
                table: "UserUploads",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppPasswords");

            migrationBuilder.DropTable(
                name: "AudioAnalyses");

            migrationBuilder.DropTable(
                name: "BackupRunFailures");

            migrationBuilder.DropTable(
                name: "CloudDropFetches");

            migrationBuilder.DropTable(
                name: "CloudSyncItems");

            migrationBuilder.DropTable(
                name: "DlnaImportItems");

            migrationBuilder.DropTable(
                name: "DuplicateCandidates");

            migrationBuilder.DropTable(
                name: "ImportItems");

            migrationBuilder.DropTable(
                name: "LibraryAccess");

            migrationBuilder.DropTable(
                name: "LibraryCloudSyncs");

            migrationBuilder.DropTable(
                name: "Lyrics");

            migrationBuilder.DropTable(
                name: "MediaFiles");

            migrationBuilder.DropTable(
                name: "MstreamImportFiles");

            migrationBuilder.DropTable(
                name: "OnlineLookups");

            migrationBuilder.DropTable(
                name: "OrganizeMoves");

            migrationBuilder.DropTable(
                name: "P2PActivities");

            migrationBuilder.DropTable(
                name: "P2PInvitations");

            migrationBuilder.DropTable(
                name: "P2PPeerLibraries");

            migrationBuilder.DropTable(
                name: "P2PRemotes");

            migrationBuilder.DropTable(
                name: "PlayEvents");

            migrationBuilder.DropTable(
                name: "PlaylistCollaborators");

            migrationBuilder.DropTable(
                name: "PlaylistEntries");

            migrationBuilder.DropTable(
                name: "RestoreJobs");

            migrationBuilder.DropTable(
                name: "ScanJobs");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TrackAnalyses");

            migrationBuilder.DropTable(
                name: "TrackAnalysisRequests");

            migrationBuilder.DropTable(
                name: "TrackArtists");

            migrationBuilder.DropTable(
                name: "TrackGenres");

            migrationBuilder.DropTable(
                name: "TrackPlayStats");

            migrationBuilder.DropTable(
                name: "TrackRatings");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "UserUploads");

            migrationBuilder.DropTable(
                name: "BackupRuns");

            migrationBuilder.DropTable(
                name: "DlnaImports");

            migrationBuilder.DropTable(
                name: "DropFolders");

            migrationBuilder.DropTable(
                name: "MstreamImports");

            migrationBuilder.DropTable(
                name: "OrganizeRuns");

            migrationBuilder.DropTable(
                name: "P2PPeers");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Tracks");

            migrationBuilder.DropTable(
                name: "Backups");

            migrationBuilder.DropTable(
                name: "LibrarySources");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropTable(
                name: "BackupDestinations");

            migrationBuilder.DropTable(
                name: "Libraries");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropTable(
                name: "Artworks");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
