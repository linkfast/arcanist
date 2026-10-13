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
    public DbSet<TrackRating> TrackRatings => Set<TrackRating>();
    public DbSet<PlayEvent> PlayEvents => Set<PlayEvent>();
    public DbSet<TrackPlayStats> TrackPlayStats => Set<TrackPlayStats>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistEntry> PlaylistEntries => Set<PlaylistEntry>();
    public DbSet<PlaylistCollaborator> PlaylistCollaborators => Set<PlaylistCollaborator>();

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

        modelBuilder.Entity<TrackRating>(rating =>
        {
            rating.HasKey(r => new { r.UserId, r.TrackId });
            rating.HasOne(r => r.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            rating.HasOne(r => r.Track).WithMany().OnDelete(DeleteBehavior.Cascade);
            rating.HasIndex(r => new { r.UserId, r.Rating, r.RatedAt });
            rating.HasIndex(r => r.TrackId);
        });

        modelBuilder.Entity<PlayEvent>(play =>
        {
            play.HasOne(p => p.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            play.HasOne(p => p.Track).WithMany().OnDelete(DeleteBehavior.SetNull);
            play.Property(p => p.Client).HasMaxLength(NameLength);
            play.HasIndex(p => new { p.UserId, p.PlayedAt });
            play.HasIndex(p => p.PlayedAt);
            play.HasIndex(p => p.TrackId);
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
