using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Arcanist.Core.Data;

/// <summary>
/// Provider-agnostic model. Each supported database has a concrete subclass with its own migrations
/// (<see cref="SqliteArcanistDbContext"/>, <see cref="PostgresArcanistDbContext"/>); every model change
/// needs a migration for both. Keep this model free of provider-specific SQL.
/// </summary>
public abstract class ArcanistDbContext(DbContextOptions options) : DbContext(options)
{
    private const int NameLength = 512;
    private const int PathLength = 4096;
    private const int EnumLength = 32;
    private const int TemplateLength = 1024;
    public const int SearchTextLength = 2000;

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<LibrarySource> LibrarySources => Set<LibrarySource>();
    public DbSet<LibraryAccess> LibraryAccess => Set<LibraryAccess>();
    public DbSet<ScanJob> ScanJobs => Set<ScanJob>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Recording> Recordings => Set<Recording>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackArtist> TrackArtists => Set<TrackArtist>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<TrackGenre> TrackGenres => Set<TrackGenre>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<Lyrics> Lyrics => Set<Lyrics>();
    public DbSet<Artwork> Artworks => Set<Artwork>();
    public DbSet<AudioAnalysis> AudioAnalyses => Set<AudioAnalysis>();
    public DbSet<DuplicateCandidate> DuplicateCandidates => Set<DuplicateCandidate>();
    public DbSet<OnlineLookup> OnlineLookups => Set<OnlineLookup>();
    public DbSet<DropFolder> DropFolders => Set<DropFolder>();
    public DbSet<ImportItem> ImportItems => Set<ImportItem>();
    public DbSet<OrganizeRun> OrganizeRuns => Set<OrganizeRun>();
    public DbSet<OrganizeMove> OrganizeMoves => Set<OrganizeMove>();
    public DbSet<AppPassword> AppPasswords => Set<AppPassword>();
    public DbSet<MstreamImport> MstreamImports => Set<MstreamImport>();
    public DbSet<MstreamImportFile> MstreamImportFiles => Set<MstreamImportFile>();

    public DbSet<DlnaImport> DlnaImports => Set<DlnaImport>();

    public DbSet<LibraryCloudSync> LibraryCloudSyncs => Set<LibraryCloudSync>();

    public DbSet<CloudSyncItem> CloudSyncItems => Set<CloudSyncItem>();

    public DbSet<DlnaImportItem> DlnaImportItems => Set<DlnaImportItem>();

    public DbSet<P2PPeer> P2PPeers => Set<P2PPeer>();

    public DbSet<P2PPeerLibrary> P2PPeerLibraries => Set<P2PPeerLibrary>();

    public DbSet<P2PInvitation> P2PInvitations => Set<P2PInvitation>();

    public DbSet<P2PRemote> P2PRemotes => Set<P2PRemote>();

    public DbSet<P2PActivity> P2PActivities => Set<P2PActivity>();
    public DbSet<TrackRating> TrackRatings => Set<TrackRating>();
    public DbSet<PlayEvent> PlayEvents => Set<PlayEvent>();

    public DbSet<RemotePlayEvent> RemotePlayEvents => Set<RemotePlayEvent>();

    public DbSet<CloudDropFetch> CloudDropFetches => Set<CloudDropFetch>();

    public DbSet<UserUpload> UserUploads => Set<UserUpload>();

    public DbSet<WishListItem> WishListItems => Set<WishListItem>();

    public DbSet<TrackAnalysis> TrackAnalyses => Set<TrackAnalysis>();

    public DbSet<TrackAnalysisRequest> TrackAnalysisRequests => Set<TrackAnalysisRequest>();

    public DbSet<TrackPlayStats> TrackPlayStats => Set<TrackPlayStats>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistEntry> PlaylistEntries => Set<PlaylistEntry>();
    public DbSet<PlaylistCollaborator> PlaylistCollaborators => Set<PlaylistCollaborator>();

    public DbSet<BackupDestination> BackupDestinations => Set<BackupDestination>();

    public DbSet<Backup> Backups => Set<Backup>();

    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();

    public DbSet<BackupRunFailure> BackupRunFailures => Set<BackupRunFailure>();

    public DbSet<RestoreJob> RestoreJobs => Set<RestoreJob>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot compare DateTimeOffset and Npgsql requires UTC for timestamptz: store UTC DateTime
        // everywhere and mark values read back as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUsers(modelBuilder);
        ConfigureLibraries(modelBuilder);
        ConfigureCatalog(modelBuilder);
        ConfigureOrganization(modelBuilder);
        ConfigureUserData(modelBuilder);
        ConfigureBackups(modelBuilder);

        // Keys come from Entity.Id (UUIDv7), never from the database.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.UserName).HasMaxLength(NameLength);
            user.Property(u => u.NormalizedUserName).HasMaxLength(NameLength);
            user.HasIndex(u => u.NormalizedUserName).IsUnique();
            user.Property(u => u.DisplayName).HasMaxLength(NameLength);
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(EnumLength);
            user.Property(u => u.SecurityStamp).HasMaxLength(64);
        });

        modelBuilder.Entity<UserSession>(session =>
        {
            session.HasOne(s => s.User).WithMany(u => u.Sessions).OnDelete(DeleteBehavior.Cascade);
            session.Property(s => s.RefreshTokenHash).HasMaxLength(128);
            session.HasIndex(s => s.RefreshTokenHash).IsUnique();
            session.Property(s => s.DeviceName).HasMaxLength(NameLength);
            session.Property(s => s.IpAddress).HasMaxLength(64);
        });

        modelBuilder.Entity<Setting>(setting =>
        {
            setting.HasKey(s => s.Key);
            setting.Property(s => s.Key).HasMaxLength(256);
        });
    }

    private static void ConfigureLibraries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Library>(library =>
        {
            library.Property(l => l.Name).HasMaxLength(NameLength);
            library.Property(l => l.OrganizeTemplate).HasMaxLength(TemplateLength);
            library.HasIndex(l => l.Number).IsUnique();
            library.HasOne(l => l.Owner).WithMany().HasForeignKey(l => l.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            library.Property(l => l.ManagedBy).HasMaxLength(64);
            library.Property(l => l.ManagedKey).HasMaxLength(128);
            library.HasIndex(l => new { l.ManagedBy, l.ManagedKey }).IsUnique();
        });

        modelBuilder.Entity<LibrarySource>(source =>
        {
            source.HasOne(s => s.Library).WithMany(l => l.Sources).OnDelete(DeleteBehavior.Cascade);
            source.Property(s => s.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            source.Property(s => s.Location).HasMaxLength(PathLength);
        });

        modelBuilder.Entity<LibraryAccess>(access =>
        {
            access.HasKey(a => new { a.UserId, a.LibraryId });
            access.HasOne(a => a.User).WithMany(u => u.LibraryAccess).OnDelete(DeleteBehavior.Cascade);
            access.HasOne(a => a.Library).WithMany(l => l.Access).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScanJob>(job =>
        {
            job.HasOne(j => j.Library).WithMany().OnDelete(DeleteBehavior.Cascade);
            job.Property(j => j.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(EnumLength);
            job.Property(j => j.ErrorCode).HasMaxLength(64);
            job.HasIndex(j => new { j.LibraryId, j.RequestedAt });
            job.HasIndex(j => j.Status);
        });
    }

    private static void ConfigureBackups(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<P2PPeer>().Property(p => p.BackupFolder).HasMaxLength(PathLength);
        modelBuilder.Entity<P2PInvitation>().Property(i => i.BackupFolder).HasMaxLength(PathLength);

        modelBuilder.Entity<BackupDestination>(destination =>
        {
            destination.Property(d => d.Name).HasMaxLength(NameLength);
            destination.Property(d => d.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            destination.Property(d => d.Location).HasMaxLength(PathLength);
        });

        modelBuilder.Entity<Backup>(backup =>
        {
            backup.Property(b => b.Name).HasMaxLength(NameLength);
            backup.Property(b => b.Folder).HasMaxLength(NameLength);
            backup.Property(b => b.Scope).HasConversion<string>().HasMaxLength(EnumLength);
            backup.Property(b => b.Schedule).HasConversion<string>().HasMaxLength(EnumLength);
            backup.Property(b => b.DayOfWeek).HasConversion<string>().HasMaxLength(EnumLength);
            backup.Property(b => b.TimeZone).HasMaxLength(64);
            backup.Property(b => b.ProtectedPassword).HasMaxLength(1000);
            backup.HasOne(b => b.Destination).WithMany().HasForeignKey(b => b.DestinationId).OnDelete(DeleteBehavior.Cascade);
            backup.HasMany(b => b.Runs).WithOne(r => r.Backup).HasForeignKey(r => r.BackupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BackupRun>(run =>
        {
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(EnumLength);
            run.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(EnumLength);
            run.Property(r => r.Snapshot).HasMaxLength(32);
            run.Property(r => r.ErrorCode).HasMaxLength(64);
            run.HasMany(r => r.Failures).WithOne().HasForeignKey(f => f.RunId).OnDelete(DeleteBehavior.Cascade);
            run.HasIndex(r => r.Status);
            run.HasIndex(r => new { r.BackupId, r.RequestedAt });
        });

        modelBuilder.Entity<BackupRunFailure>(failure =>
        {
            failure.Property(f => f.Path).HasMaxLength(PathLength);
            failure.Property(f => f.Code).HasMaxLength(64);
        });
        modelBuilder.Entity<RestoreJob>(job =>
        {
            job.Property(j => j.BackupFolder).HasMaxLength(NameLength);
            job.Property(j => j.BackupName).HasMaxLength(NameLength);
            job.Property(j => j.Snapshot).HasMaxLength(32);
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(EnumLength);
            job.Property(j => j.ErrorCode).HasMaxLength(64);
            job.HasOne<BackupDestination>().WithMany().HasForeignKey(j => j.DestinationId).OnDelete(DeleteBehavior.Cascade);
            job.HasIndex(j => j.Status);
        });
    }

    private static void ConfigureUserData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppPassword>(password =>
        {
            password.HasOne(p => p.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            password.Property(p => p.Name).HasMaxLength(NameLength);
            password.Property(p => p.EncryptedSecret).HasMaxLength(1000);
            password.Property(p => p.SecretHash).HasMaxLength(64);
            password.HasIndex(p => p.SecretHash).IsUnique();
            password.HasIndex(p => p.UserId);
        });

        modelBuilder.Entity<MstreamImport>(import =>
        {
            import.Property(i => i.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            import.Property(i => i.Status).HasConversion<string>().HasMaxLength(EnumLength);
            import.Property(i => i.Source).HasMaxLength(PathLength);
            import.Property(i => i.ErrorCode).HasMaxLength(64);
            import.HasMany(i => i.Files).WithOne().HasForeignKey(f => f.ImportId).OnDelete(DeleteBehavior.Cascade);
            import.HasIndex(i => i.Status);
        });

        modelBuilder.Entity<MstreamImportFile>(file =>
        {
            file.Property(f => f.Status).HasConversion<string>().HasMaxLength(EnumLength);
            file.Property(f => f.Vpath).HasMaxLength(NameLength);
            file.Property(f => f.Path).HasMaxLength(PathLength);
            file.Property(f => f.Reason).HasMaxLength(64);
            file.HasIndex(f => new { f.ImportId, f.Status });
        });

        modelBuilder.Entity<DlnaImport>(import =>
        {
            import.Property(i => i.Status).HasConversion<string>().HasMaxLength(EnumLength);
            import.Property(i => i.Location).HasMaxLength(PathLength);
            import.Property(i => i.ServerName).HasMaxLength(NameLength);
            import.Property(i => i.ErrorCode).HasMaxLength(64);
            import.HasOne(i => i.LibrarySource).WithMany().HasForeignKey(i => i.LibrarySourceId).OnDelete(DeleteBehavior.Cascade);
            import.HasMany(i => i.Items).WithOne().HasForeignKey(i => i.ImportId).OnDelete(DeleteBehavior.Cascade);
            import.HasIndex(i => i.Status);
        });

        modelBuilder.Entity<LibraryCloudSync>(sync =>
        {
            sync.HasKey(s => s.LibraryId);
            sync.HasOne(s => s.Library).WithMany().HasForeignKey(s => s.LibraryId).OnDelete(DeleteBehavior.Cascade);
            sync.Property(s => s.Mode).HasConversion<string>().HasMaxLength(EnumLength);
            sync.Property(s => s.ErrorCode).HasMaxLength(64);
        });

        modelBuilder.Entity<CloudSyncItem>(item =>
        {
            item.HasOne<Library>().WithMany().HasForeignKey(i => i.LibraryId).OnDelete(DeleteBehavior.Cascade);
            item.Property(i => i.Status).HasConversion<string>().HasMaxLength(EnumLength);
            item.Property(i => i.ContentHash).HasMaxLength(64);
            item.Property(i => i.AudioHash).HasMaxLength(64);
            item.Property(i => i.CloudTrackId).HasMaxLength(64);
            item.Property(i => i.Reason).HasMaxLength(64);
            item.Property(i => i.MatchKey).HasMaxLength(NameLength);
            item.Property(i => i.Title).HasMaxLength(NameLength);
            item.Property(i => i.Path).HasMaxLength(PathLength);
            item.HasIndex(i => new { i.LibraryId, i.ContentHash });
            item.HasIndex(i => new { i.LibraryId, i.CloudTrackId });
        });

        modelBuilder.Entity<DlnaImportItem>(item =>
        {
            item.Property(i => i.Status).HasConversion<string>().HasMaxLength(EnumLength);
            item.Property(i => i.ObjectId).HasMaxLength(PathLength);
            item.Property(i => i.Url).HasMaxLength(PathLength);
            item.Property(i => i.Extension).HasMaxLength(16);
            item.Property(i => i.Title).HasMaxLength(NameLength);
            item.Property(i => i.Artist).HasMaxLength(NameLength);
            item.Property(i => i.AlbumArtist).HasMaxLength(NameLength);
            item.Property(i => i.Album).HasMaxLength(NameLength);
            item.Property(i => i.Genre).HasMaxLength(NameLength);
            item.Property(i => i.ArtUrl).HasMaxLength(PathLength);
            item.Property(i => i.Reason).HasMaxLength(64);
            item.Property(i => i.DestinationPath).HasMaxLength(PathLength);
            item.HasIndex(i => new { i.ImportId, i.Status });
        });

        modelBuilder.Entity<P2PPeer>(peer =>
        {
            peer.Property(p => p.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            peer.Property(p => p.Source).HasConversion<string>().HasMaxLength(EnumLength);
            peer.Property(p => p.NodeId).HasMaxLength(64);
            peer.Property(p => p.Name).HasMaxLength(NameLength);
            peer.HasIndex(p => p.NodeId).IsUnique();
            peer.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            peer.ComplexProperty(p => p.Rules);
        });

        modelBuilder.Entity<P2PPeerLibrary>(grant =>
        {
            grant.HasKey(g => new { g.PeerId, g.LibraryId });
            grant.HasOne(g => g.Peer).WithMany(p => p.Libraries).HasForeignKey(g => g.PeerId).OnDelete(DeleteBehavior.Cascade);
            grant.HasOne(g => g.Library).WithMany().HasForeignKey(g => g.LibraryId).OnDelete(DeleteBehavior.Cascade);
            grant.HasIndex(g => g.LibraryId);
        });

        modelBuilder.Entity<P2PInvitation>(invitation =>
        {
            invitation.Property(i => i.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            invitation.Property(i => i.Name).HasMaxLength(NameLength);
            invitation.Property(i => i.TokenHash).HasMaxLength(64);
            invitation.Property(i => i.LibraryIds).HasMaxLength(4000);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.HasIndex(i => i.ExpiresAt);
            invitation.ComplexProperty(i => i.Rules);
        });

        modelBuilder.Entity<P2PRemote>(remote =>
        {
            remote.Property(r => r.NodeId).HasMaxLength(64);
            remote.Property(r => r.Ticket).HasMaxLength(4000);
            remote.Property(r => r.Name).HasMaxLength(NameLength);
            remote.Property(r => r.Status).HasConversion<string>().HasMaxLength(EnumLength);
            remote.HasIndex(r => r.NodeId).IsUnique();
            remote.HasOne(r => r.Library).WithMany().HasForeignKey(r => r.LibraryId).OnDelete(DeleteBehavior.Cascade);
            remote.HasIndex(r => r.LibraryId).IsUnique();
            remote.ComplexProperty(r => r.Rules);
        });

        modelBuilder.Entity<P2PActivity>(activity =>
        {
            activity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            activity.Property(a => a.PeerName).HasMaxLength(NameLength);
            activity.Property(a => a.Detail).HasMaxLength(500);
            activity.HasIndex(a => a.At);
            activity.HasIndex(a => new { a.PeerId, a.At });
        });

        modelBuilder.Entity<TrackRating>(rating =>
        {
            rating.HasKey(r => new { r.UserId, r.TrackId });
            rating.HasOne(r => r.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            rating.HasOne(r => r.Track).WithMany().OnDelete(DeleteBehavior.Cascade);
            rating.HasIndex(r => new { r.UserId, r.Rating, r.RatedAt });
            rating.HasIndex(r => r.TrackId);
        });

        modelBuilder.Entity<CloudDropFetch>(fetch =>
        {
            fetch.HasOne(f => f.DropFolder).WithMany().OnDelete(DeleteBehavior.Cascade);
            fetch.Property(f => f.CloudTrackId).HasMaxLength(64);
            fetch.Property(f => f.FileName).HasMaxLength(512);
            fetch.Property(f => f.Title).HasMaxLength(500);
            fetch.Property(f => f.ErrorCode).HasMaxLength(NameLength);
            fetch.Property(f => f.LibraryPath).HasMaxLength(2048);
            fetch.Property(f => f.UploadedTrackId).HasMaxLength(64);
            fetch.HasIndex(f => new { f.DropFolderId, f.CloudTrackId }).IsUnique();
        });

        modelBuilder.Entity<TrackAnalysis>(analysis =>
        {
            analysis.Property(a => a.ContentHash).HasMaxLength(64);
            analysis.HasIndex(a => a.ContentHash).IsUnique();
            analysis.HasIndex(a => a.AnalyzedAt);
            analysis.Property(a => a.State).HasConversion<string>().HasMaxLength(EnumLength);
            analysis.Property(a => a.Error).HasMaxLength(NameLength);
        });

        modelBuilder.Entity<TrackAnalysisRequest>(request =>
        {
            request.Property(r => r.ContentHash).HasMaxLength(64);
            request.HasIndex(r => r.ContentHash).IsUnique();
            request.HasIndex(r => new { r.Priority, r.RequestedAt });
        });

        modelBuilder.Entity<UserUpload>(upload =>
        {
            upload.HasOne(u => u.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            upload.HasOne<Library>().WithMany().HasForeignKey(u => u.LibraryId).OnDelete(DeleteBehavior.SetNull);
            upload.HasOne<Track>().WithMany().HasForeignKey(u => u.TrackId).OnDelete(DeleteBehavior.SetNull);
            upload.Property(u => u.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            upload.Property(u => u.Status).HasConversion<string>().HasMaxLength(EnumLength);
            upload.Property(u => u.Name).HasMaxLength(512);
            upload.Property(u => u.ContentHash).HasMaxLength(64);
            upload.Property(u => u.Path).HasMaxLength(2048);
            upload.Property(u => u.CloudTrackId).HasMaxLength(64);
            upload.Property(u => u.Source).HasMaxLength(64);
            upload.Property(u => u.Client).HasConversion<string>().HasMaxLength(EnumLength);
            upload.Property(u => u.ErrorCode).HasMaxLength(NameLength);
            upload.HasIndex(u => new { u.UserId, u.CreatedAt });
            upload.HasIndex(u => new { u.Kind, u.Status });
        });

        modelBuilder.Entity<WishListItem>(wish =>
        {
            wish.HasOne(w => w.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            wish.HasOne<Track>().WithMany().HasForeignKey(w => w.TrackId).OnDelete(DeleteBehavior.SetNull);
            wish.Property(w => w.Title).HasMaxLength(NameLength);
            wish.Property(w => w.Artist).HasMaxLength(NameLength);
            wish.Property(w => w.Album).HasMaxLength(NameLength);
            wish.Property(w => w.Note).HasMaxLength(1000);
            wish.Property(w => w.MatchedBy).HasConversion<string>().HasMaxLength(EnumLength);
            wish.HasIndex(w => new { w.UserId, w.CreatedAt });
            wish.HasIndex(w => w.ObtainedAt);
        });

        modelBuilder.Entity<RemotePlayEvent>(play =>
        {
            play.HasOne(p => p.Peer).WithMany().HasForeignKey(p => p.PeerId).OnDelete(DeleteBehavior.Cascade);
            play.HasOne(p => p.Track).WithMany().HasForeignKey(p => p.TrackId).OnDelete(DeleteBehavior.SetNull);
            play.Property(p => p.RemoteUser).HasMaxLength(NameLength);
            play.Property(p => p.ExternalId).HasMaxLength(128);
            play.HasIndex(p => new { p.PeerId, p.ExternalId }).IsUnique();
            play.HasIndex(p => new { p.TrackId, p.PlayedAt });
            play.HasIndex(p => p.PlayedAt);
        });

        modelBuilder.Entity<PlayEvent>(play =>
        {
            play.HasOne(p => p.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            play.HasOne(p => p.Track).WithMany().OnDelete(DeleteBehavior.SetNull);
            play.Property(p => p.Client).HasMaxLength(NameLength);
            play.Property(p => p.ExternalId).HasMaxLength(128);
            play.Property(p => p.Source).HasMaxLength(32);
            play.HasIndex(p => new { p.UserId, p.PlayedAt });
            play.HasIndex(p => new { p.UserId, p.ExternalId });
            play.HasIndex(p => p.PlayedAt);
            play.HasIndex(p => new { p.TrackId, p.PlayedAt });
        });

        modelBuilder.Entity<TrackPlayStats>(stats =>
        {
            stats.HasKey(s => new { s.UserId, s.TrackId });
            stats.HasOne(s => s.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            stats.HasOne(s => s.Track).WithMany().OnDelete(DeleteBehavior.Cascade);
            stats.HasIndex(s => new { s.UserId, s.LastPlayedAt });
            stats.HasIndex(s => s.TrackId);
        });

        modelBuilder.Entity<Playlist>(playlist =>
        {
            playlist.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
            playlist.Property(p => p.Name).HasMaxLength(NameLength);
            playlist.Property(p => p.NormalizedName).HasMaxLength(NameLength);
            playlist.Property(p => p.Description).HasMaxLength(2000);
            playlist.Property(p => p.SearchText).HasMaxLength(SearchTextLength);
            playlist.HasIndex(p => p.OwnerId);
            playlist.HasIndex(p => p.NormalizedName);
        });

        modelBuilder.Entity<PlaylistEntry>(entry =>
        {
            entry.HasOne(e => e.Playlist).WithMany(p => p.Entries).OnDelete(DeleteBehavior.Cascade);
            entry.HasOne(e => e.Track).WithMany().OnDelete(DeleteBehavior.Cascade);
            entry.HasIndex(e => new { e.PlaylistId, e.Position });
            entry.HasIndex(e => e.TrackId);
        });

        modelBuilder.Entity<PlaylistCollaborator>(collaborator =>
        {
            collaborator.HasKey(c => new { c.PlaylistId, c.UserId });
            collaborator.HasOne(c => c.Playlist).WithMany(p => p.Collaborators).OnDelete(DeleteBehavior.Cascade);
            collaborator.HasOne(c => c.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            collaborator.HasIndex(c => c.UserId);
        });
    }

    private static void ConfigureOrganization(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DropFolder>(folder =>
        {
            folder.HasOne(f => f.LibrarySource).WithMany().OnDelete(DeleteBehavior.Cascade);
            folder.Property(f => f.Path).HasMaxLength(PathLength);
            folder.Property(f => f.DuplicatePolicy).HasConversion<string>().HasMaxLength(EnumLength);
            folder.Property(f => f.ConvertTo).HasConversion<string>().HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<ImportItem>(item =>
        {
            item.HasOne(i => i.DropFolder).WithMany().OnDelete(DeleteBehavior.Cascade);
            item.Property(i => i.RelativePath).HasMaxLength(PathLength);
            item.Property(i => i.Status).HasConversion<string>().HasMaxLength(EnumLength);
            item.Property(i => i.Reason).HasConversion<string>().HasMaxLength(EnumLength);
            item.Property(i => i.ContentHash).HasMaxLength(64);
            item.Property(i => i.Tags).HasMaxLength(8000);
            item.Property(i => i.Request).HasMaxLength(8000);
            item.Property(i => i.DestinationPath).HasMaxLength(PathLength);
            item.Property(i => i.SuggestionState).HasConversion<string>().HasMaxLength(EnumLength);
            item.Property(i => i.SuggestionsFor).HasMaxLength(64);
            item.Property(i => i.Suggestions).HasMaxLength(16000);
            item.HasIndex(i => new { i.DropFolderId, i.RelativePath });
            item.HasIndex(i => new { i.Status, i.UpdatedAt });
            item.HasIndex(i => i.ContentHash);
        });

        modelBuilder.Entity<OrganizeRun>(run =>
        {
            run.HasOne(r => r.Library).WithMany().OnDelete(DeleteBehavior.Cascade);
            run.Property(r => r.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(EnumLength);
            run.Property(r => r.Template).HasMaxLength(TemplateLength);
            run.Property(r => r.ErrorCode).HasMaxLength(64);
            run.HasIndex(r => new { r.LibraryId, r.StartedAt });
        });

        modelBuilder.Entity<OrganizeMove>(move =>
        {
            move.HasOne(m => m.Run).WithMany(r => r.Moves).OnDelete(DeleteBehavior.Cascade);
            // No foreign key to the source: when a location is removed, its moves stay in the run's record and
            // undoing them finds nothing to move back.
            move.Property(m => m.FromPath).HasMaxLength(PathLength);
            move.Property(m => m.ToPath).HasMaxLength(PathLength);
            move.HasIndex(m => new { m.RunId, m.Sequence });
        });
    }

    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Artist>(artist =>
        {
            artist.Property(a => a.Name).HasMaxLength(NameLength);
            artist.Property(a => a.NormalizedName).HasMaxLength(NameLength);
            artist.Property(a => a.SortName).HasMaxLength(NameLength);
            artist.HasIndex(a => a.NormalizedName);
            artist.Property(x => x.SearchText).HasMaxLength(SearchTextLength);
            artist.HasIndex(a => a.MusicBrainzId).IsUnique();
        });

        modelBuilder.Entity<Album>(album =>
        {
            album.Property(a => a.Title).HasMaxLength(NameLength);
            album.Property(a => a.NormalizedTitle).HasMaxLength(NameLength);
            album.HasOne(a => a.AlbumArtist).WithMany().OnDelete(DeleteBehavior.Restrict);
            album.HasOne(a => a.Artwork).WithMany().OnDelete(DeleteBehavior.SetNull);
            album.HasIndex(a => new { a.AlbumArtistId, a.NormalizedTitle });
            album.Property(x => x.SearchText).HasMaxLength(SearchTextLength);
            album.HasIndex(a => a.MusicBrainzReleaseId);
        });

        modelBuilder.Entity<Recording>(recording =>
        {
            recording.HasIndex(r => r.MusicBrainzId).IsUnique();
            recording.HasIndex(r => r.AcoustId);
        });

        modelBuilder.Entity<Track>(track =>
        {
            track.Property(t => t.Title).HasMaxLength(NameLength);
            track.Property(t => t.NormalizedTitle).HasMaxLength(NameLength);
            track.HasOne(t => t.Album).WithMany(a => a.Tracks).OnDelete(DeleteBehavior.Restrict);
            track.HasOne(t => t.Recording).WithMany(r => r.Tracks).OnDelete(DeleteBehavior.Restrict);
            track.HasOne(t => t.Artwork).WithMany().OnDelete(DeleteBehavior.SetNull);
            track.HasIndex(t => t.NormalizedTitle);
            track.Property(x => x.SearchText).HasMaxLength(SearchTextLength);
            track.HasIndex(t => new { t.AlbumId, t.DiscNumber, t.TrackNumber });
        });

        modelBuilder.Entity<TrackArtist>(trackArtist =>
        {
            trackArtist.HasKey(ta => new { ta.TrackId, ta.ArtistId, ta.Role });
            trackArtist.HasOne(ta => ta.Track).WithMany(t => t.Artists).OnDelete(DeleteBehavior.Cascade);
            trackArtist.HasOne(ta => ta.Artist).WithMany().OnDelete(DeleteBehavior.Restrict);
            trackArtist.Property(ta => ta.Role).HasConversion<string>().HasMaxLength(EnumLength);
            trackArtist.HasIndex(ta => ta.ArtistId);
        });

        modelBuilder.Entity<Genre>(genre =>
        {
            genre.Property(g => g.Name).HasMaxLength(NameLength);
            genre.Property(g => g.NormalizedName).HasMaxLength(NameLength);
            genre.HasIndex(g => g.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<TrackGenre>(trackGenre =>
        {
            trackGenre.HasKey(tg => new { tg.TrackId, tg.GenreId });
            trackGenre.HasOne(tg => tg.Track).WithMany(t => t.Genres).OnDelete(DeleteBehavior.Cascade);
            trackGenre.HasOne(tg => tg.Genre).WithMany().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaFile>(file =>
        {
            file.HasOne(f => f.Track).WithMany(t => t.Files).OnDelete(DeleteBehavior.Restrict);
            file.HasOne(f => f.LibrarySource).WithMany().OnDelete(DeleteBehavior.Cascade);
            file.Property(f => f.RelativePath).HasMaxLength(PathLength);
            file.Property(f => f.ContentHash).HasMaxLength(64);
            file.Property(f => f.Format).HasMaxLength(64);
            file.HasIndex(f => new { f.LibrarySourceId, f.RelativePath, f.CueTrack }).IsUnique();
            file.HasIndex(f => f.ContentHash);
            file.HasIndex(f => f.TrackId);
        });

        modelBuilder.Entity<AudioAnalysis>(analysis =>
        {
            // No foreign key to MediaFile: the analysis belongs to the audio, and outlives any one copy.
            analysis.Property(a => a.ContentHash).HasMaxLength(64);
            analysis.HasIndex(a => a.ContentHash).IsUnique();
            analysis.Property(a => a.State).HasConversion<string>().HasMaxLength(EnumLength);
            analysis.Property(a => a.Error).HasMaxLength(500);
            analysis.Property(a => a.AcoustIdFingerprint).HasMaxLength(8000);
            analysis.Property(a => a.LoudnessHistogram).HasMaxLength(8000);
        });

        modelBuilder.Entity<DuplicateCandidate>(candidate =>
        {
            // No foreign keys: a candidate whose track disappeared is removed with it by the cleaner.
            candidate.Property(c => c.Status).HasConversion<string>().HasMaxLength(EnumLength);
            candidate.HasIndex(c => new { c.TrackAId, c.TrackBId }).IsUnique();
            candidate.HasIndex(c => c.TrackBId);
            candidate.HasIndex(c => c.Status);
        });

        modelBuilder.Entity<Lyrics>(lyrics =>
        {
            lyrics.HasKey(l => l.TrackId);
            lyrics.HasOne(l => l.Track).WithOne().HasForeignKey<Lyrics>(l => l.TrackId).OnDelete(DeleteBehavior.Cascade);
            lyrics.Property(l => l.Source).HasConversion<string>().HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<OnlineLookup>(lookup =>
        {
            lookup.Property(l => l.Service).HasConversion<string>().HasMaxLength(EnumLength);
            lookup.Property(l => l.Outcome).HasConversion<string>().HasMaxLength(EnumLength);
            lookup.Property(l => l.Subject).HasMaxLength(64);
            lookup.HasIndex(l => new { l.Service, l.Subject }).IsUnique();
        });

        modelBuilder.Entity<Artwork>(artwork =>
        {
            artwork.Property(a => a.Hash).HasMaxLength(64);
            artwork.Property(a => a.MimeType).HasMaxLength(64);
            artwork.Property(a => a.Source).HasConversion<string>().HasMaxLength(EnumLength);
            artwork.HasIndex(a => a.Hash).IsUnique();
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
