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
    public DbSet<Artwork> Artworks => Set<Artwork>();

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
            job.HasIndex(j => new { j.LibraryId, j.RequestedAt });
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
            artist.HasIndex(a => a.MusicBrainzId).IsUnique();
        });

        modelBuilder.Entity<Album>(album =>
        {
            album.Property(a => a.Title).HasMaxLength(NameLength);
            album.Property(a => a.NormalizedTitle).HasMaxLength(NameLength);
            album.HasOne(a => a.AlbumArtist).WithMany().OnDelete(DeleteBehavior.Restrict);
            album.HasOne(a => a.Artwork).WithMany().OnDelete(DeleteBehavior.SetNull);
            album.HasIndex(a => new { a.AlbumArtistId, a.NormalizedTitle });
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
            file.HasIndex(f => new { f.LibrarySourceId, f.RelativePath }).IsUnique();
            file.HasIndex(f => f.ContentHash);
            file.HasIndex(f => f.TrackId);
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
