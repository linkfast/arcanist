using Keyset = Arcanist.Core.Application.Catalog.Keyset;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Tests.Data;

/// <summary>
/// The same assertions run against every supported provider, so a model change that only works on one of
/// them, or a migration generated for only one, fails here.
/// </summary>
public abstract class DatabaseProviderTests
{
    private static readonly DateTime Moment = new(2026, 10, 10, 12, 34, 56, 789, DateTimeKind.Utc);

    /// <summary>Returns a context over a freshly migrated, empty database. Repeated calls reach the same database.</summary>
    protected abstract Task<ArcanistDbContext> OpenAsync();

    [Fact]
    public async Task Migrations_are_up_to_date_with_the_model()
    {
        await using var db = await OpenAsync();

        Assert.False(db.Database.HasPendingModelChanges(),
            $"Model changed without a migration for {db.GetType().Name}; run `dotnet ef migrations add` for both providers.");
    }

    [Fact]
    public async Task Round_trips_a_catalog_with_users_and_libraries()
    {
        var ids = await SeedAsync();

        await using var db = await OpenAsync();

        var user = await db.Users.Include(u => u.LibraryAccess).SingleAsync(u => u.Id == ids.UserId, TestContext.Current.CancellationToken);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(Permissions.ManageLibraries | Permissions.Download, user.Permissions);
        Assert.Equal(Moment, user.LastLoginAt);
        Assert.Equal(DateTimeKind.Utc, user.LastLoginAt!.Value.Kind);
        Assert.Single(user.LibraryAccess);

        var job = await db.ScanJobs.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ScanStatus.Completed, job.Status);
        Assert.Equal(DateTimeKind.Utc, job.RequestedAt.Kind);

        var track = await db.Tracks
            .Include(t => t.Files)
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .Include(t => t.Genres).ThenInclude(g => g.Genre)
            .Include(t => t.Album).ThenInclude(a => a.Artwork)
            .Include(t => t.Recording)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ids.TrackId, track.Id);
        Assert.Equal(2, track.Files.Count);
        Assert.All(track.Files, f => Assert.Equal(Moment, f.ModifiedAt));
        Assert.Equal([ArtistRole.Primary, ArtistRole.Featured], track.Artists.OrderBy(a => a.Position).Select(a => a.Role));
        Assert.Equal("rock", Assert.Single(track.Genres).Genre.NormalizedName);
        Assert.Equal(ArtworkSource.Embedded, track.Album.Artwork!.Source);
        Assert.Equal(ids.RecordingMbid, track.Recording.MusicBrainzId);
    }

    [Fact]
    public async Task Normalized_user_names_are_unique()
    {
        await using (var db = await OpenAsync())
        {
            db.Users.Add(NewUser("Alice"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = await OpenAsync())
        {
            db.Users.Add(NewUser("ALICE"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Deleting_a_library_removes_its_sources_files_and_access_but_keeps_the_catalog()
    {
        var ids = await SeedAsync();

        await using (var db = await OpenAsync())
        {
            await db.Libraries.Where(l => l.Id == ids.LibraryId).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = await OpenAsync())
        {
            Assert.Empty(await db.LibrarySources.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await db.MediaFiles.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await db.LibraryAccess.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await db.ScanJobs.ToListAsync(TestContext.Current.CancellationToken));
            // Orphaned tracks are purged by the scanner, not by the database.
            Assert.Single(await db.Tracks.ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Keys_are_generated_by_the_application_as_uuid_v7()
    {
        var ids = await SeedAsync();

        Assert.Equal(7, ids.TrackId.Version);
        await using var db = await OpenAsync();
        Assert.True(await db.Tracks.AnyAsync(t => t.Id == ids.TrackId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Keyset_pages_visit_every_row_once_even_with_equal_keys()
    {
        await using (var db = await OpenAsync())
        {
            // Equal names force the id tie-breaker; mixed case and accents check the provider's ordering.
            foreach (var name in new[] { "b", "a", "b", "c", "b", "á", "B", "a" })
                db.Artists.Add(new Artist { Name = name, NormalizedName = name });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var db = await OpenAsync();
            var (page, next, total) = await Keyset.PageAsync(db.Artists, a => a.NormalizedName, cursor, 3, TestContext.Current.CancellationToken);
            Assert.Equal(8, total);
            seen.AddRange(page.Select(a => a.Id));
            cursor = next;
        }
        while (cursor is not null);

        Assert.Equal(8, seen.Count);
        Assert.Equal(8, seen.Distinct().Count());
    }

    [Fact]
    public async Task Full_text_search_matches_word_starts_and_follows_changes()
    {
        var ids = await SeedAsync();
        var cancel = TestContext.Current.CancellationToken;

        await using (var db = await OpenAsync())
        {
            await FullTextSearch.EnsureAsync(db, cancel);
            // Running it again finds everything in place.
            await FullTextSearch.EnsureAsync(db, cancel);
            await SearchIndexer.RefreshAsync(db, cancel);
        }

        async Task<List<Guid>> FindAsync(SearchKind kind, string query)
        {
            await using var db = await OpenAsync();
            return await FullTextSearch.Match(db, kind, query)!.OrderByDescending(h => h.Score).Select(h => h.Id).ToListAsync(cancel);
        }

        // A track is found by its title, artists and album; accents and case never matter.
        Assert.Equal([ids.TrackId], await FindAsync(SearchKind.Tracks, "SVEFN"));
        Assert.Equal([ids.TrackId], await FindAsync(SearchKind.Tracks, "sigur agaet"));
        Assert.Equal([ids.TrackId], await FindAsync(SearchKind.Tracks, "guest"));
        Assert.Empty(await FindAsync(SearchKind.Tracks, "sigur nothing"));
        Assert.Single(await FindAsync(SearchKind.Albums, "ágætis rós"));
        Assert.Single(await FindAsync(SearchKind.Artists, "Ros"));
        await using (var db = await OpenAsync())
            Assert.Null(FullTextSearch.Match(db, SearchKind.Tracks, "  ¿?!  "));

        // Quotes and operators in a query are words, never syntax.
        Assert.Empty(await FindAsync(SearchKind.Tracks, "\"sigur\" OR NEAR(* -"));

        // Changes reach the index through the database.
        await using (var db = await OpenAsync())
        {
            await db.Tracks.Where(t => t.Id == ids.TrackId).ExecuteUpdateAsync(s => s.SetProperty(t => t.SearchText, (string?)null), cancel);
            await db.Tracks.Where(t => t.Id == ids.TrackId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, "Starálfur"), cancel);
            await SearchIndexer.RefreshAsync(db, cancel);
        }

        Assert.Empty(await FindAsync(SearchKind.Tracks, "svefn"));
        Assert.Equal([ids.TrackId], await FindAsync(SearchKind.Tracks, "staralfur"));

        await using (var db = await OpenAsync())
        {
            await db.MediaFiles.ExecuteDeleteAsync(cancel);
            await db.TrackArtists.ExecuteDeleteAsync(cancel);
            await db.TrackGenres.ExecuteDeleteAsync(cancel);
            await db.Tracks.ExecuteDeleteAsync(cancel);
        }

        Assert.Empty(await FindAsync(SearchKind.Tracks, "staralfur"));
    }

    private async Task<SeedIds> SeedAsync()
    {
        await using var db = await OpenAsync();

        var user = NewUser("admin");
        user.Role = UserRole.Admin;
        user.Permissions = Permissions.ManageLibraries | Permissions.Download;
        user.LastLoginAt = Moment;

        var library = new Library { Name = "Music" };
        var source = new LibrarySource { Library = library, Kind = LibrarySourceKind.Local, Location = "/music" };
        library.Sources.Add(source);
        library.Access.Add(new LibraryAccess { User = user, Library = library });

        var artist = new Artist { Name = "Sigur Rós", NormalizedName = TextNormalizer.Normalize("Sigur Rós") };
        var guest = new Artist { Name = "Guest", NormalizedName = "guest" };
        var artwork = new Artwork { Hash = new string('a', 64), MimeType = "image/jpeg", Width = 600, Height = 600, Source = ArtworkSource.Embedded };
        var album = new Album { Title = "Ágætis byrjun", NormalizedTitle = TextNormalizer.Normalize("Ágætis byrjun"), AlbumArtist = artist, Artwork = artwork, Year = 1999 };
        var recording = new Recording { MusicBrainzId = Guid.NewGuid() };
        var genre = new Genre { Name = "Rock", NormalizedName = "rock" };

        var track = new Track
        {
            Title = "Svefn-g-englar",
            NormalizedTitle = TextNormalizer.Normalize("Svefn-g-englar"),
            Album = album,
            Recording = recording,
            DiscNumber = 1,
            TrackNumber = 2,
            DurationMs = 604_000,
        };
        track.Artists.Add(new TrackArtist { Track = track, Artist = artist, Role = ArtistRole.Primary, Position = 0 });
        track.Artists.Add(new TrackArtist { Track = track, Artist = guest, Role = ArtistRole.Featured, Position = 1 });
        track.Genres.Add(new TrackGenre { Track = track, Genre = genre });

        // The same recording twice (FLAC and MP3) is one Track with two files.
        foreach (var (path, format) in new[] { ("a/02.flac", "FLAC"), ("b/02.mp3", "MPEG-1 Layer 3") })
        {
            track.Files.Add(new MediaFile
            {
                Track = track,
                LibrarySource = source,
                RelativePath = path,
                Format = format,
                ContentHash = Guid.NewGuid().ToString("N"),
                Size = 1_000,
                ModifiedAt = Moment,
                DurationMs = 604_000,
            });
        }

        db.AddRange(user, library, track);
        db.ScanJobs.Add(new ScanJob { Library = library, Kind = ScanKind.Full, Status = ScanStatus.Completed });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new SeedIds(user.Id, library.Id, track.Id, recording.MusicBrainzId!.Value);
    }

    private static User NewUser(string name) => new()
    {
        UserName = name,
        NormalizedUserName = TextNormalizer.Normalize(name),
        PasswordHash = "not-a-real-hash",
    };

    private sealed record SeedIds(Guid UserId, Guid LibraryId, Guid TrackId, Guid RecordingMbid);
}
