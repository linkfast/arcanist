using System.Security.Claims;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.P2P;

// The peer API: what a server that receives a share asks the server that shares, through the P2P network. Its
// types are part of the server-to-server contract (in the OpenAPI document, tag "Peer"); players never use it.

/// <summary>A shared library, as the receiving server sees it.</summary>
public sealed record PeerLibrary(Guid Id, string Name);

/// <summary>What the sharing server tells about itself and the share.</summary>
public sealed record PeerShareInfo(string Name, P2PRules Rules, IReadOnlyList<PeerLibrary> Libraries, DateTime? ExpiresAt);

/// <summary>
/// One shared file, described so the receiving server can catalog it without reading it. <see cref="Path"/> starts
/// with the name of its library (and <c>-2</c>, <c>-3</c>… for further folders of it), so paths never collide.
/// </summary>
public sealed record PeerFile(
    Guid Id,
    string Path,
    int CueTrack,
    int? StartMs,
    int? EndMs,
    long Size,
    DateTime ModifiedAt,
    string ContentHash,
    string? Artwork,
    AudioMetadata Metadata);

public sealed record PeerFilePage(IReadOnlyList<PeerFile> Items, Guid? NextCursor);

/// <summary>Serves the peer API to server peers, within the libraries shared with each.</summary>
public sealed class PeerApi(IArcanistDbContextFactory databases, P2PSettings settings)
{
    public const int PageSize = 500;

    /// <summary>Makes the request act as the peer, for the application services: it sees the shared libraries only.</summary>
    public static UserAccess SignIn(HttpContext context, P2PPeerSnapshot peer)
    {
        var access = new UserAccess(peer.Id, "peer:" + peer.NodeId[..12], peer.Name, UserRole.User,
            peer.Rules.AllowDownload ? Permissions.Download : Permissions.None, "", false, peer.LibraryIds);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ArcanistClaims.UserId, access.UserId.ToString()),
            new Claim(ArcanistClaims.SessionId, Guid.Empty.ToString()),
        ], "p2p"));
        context.Items[typeof(UserAccess)] = access;
        return access;
    }

    public async Task<PeerShareInfo> InfoAsync(P2PPeerSnapshot peer, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var libraries = await db.Libraries.AsNoTracking().Where(l => peer.LibraryIds.Contains(l.Id))
            .OrderBy(l => l.Number).Select(l => new PeerLibrary(l.Id, l.Name)).ToListAsync(cancellationToken);
        var config = await settings.GetAsync(cancellationToken);
        return new PeerShareInfo(config.DisplayName, peer.Rules, libraries, peer.ExpiresAt);
    }

    /// <summary>The shared files present now, in a stable order, a page at a time.</summary>
    public async Task<PeerFilePage> FilesAsync(P2PPeerSnapshot peer, Guid? after, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var prefixes = await PrefixesAsync(db, peer, cancellationToken);
        var query = db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && prefixes.Keys.Contains(f.LibrarySourceId));
        if (after is { } cursor)
            query = query.Where(f => f.Id.CompareTo(cursor) > 0);

        var files = await query.OrderBy(f => f.Id).Take(PageSize + 1)
            .Include(f => f.Track).ThenInclude(t => t.Album).ThenInclude(a => a.AlbumArtist)
            .Include(f => f.Track).ThenInclude(t => t.Album).ThenInclude(a => a.Artwork)
            .Include(f => f.Track).ThenInclude(t => t.Artwork)
            .Include(f => f.Track).ThenInclude(t => t.Recording)
            .Include(f => f.Track).ThenInclude(t => t.Artists.OrderBy(a => a.Position)).ThenInclude(a => a.Artist)
            .Include(f => f.Track).ThenInclude(t => t.Genres).ThenInclude(g => g.Genre)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var page = files.Take(PageSize).ToList();
        var trackIds = page.Select(f => f.TrackId).Distinct().ToList();
        var lyrics = await db.Lyrics.AsNoTracking().Where(l => trackIds.Contains(l.TrackId)).ToDictionaryAsync(l => l.TrackId, cancellationToken);

        return new PeerFilePage(
            page.Select(f => Describe(f, prefixes[f.LibrarySourceId], lyrics.GetValueOrDefault(f.TrackId))).ToList(),
            files.Count > PageSize ? page[^1].Id : null);
    }

    /// <summary>The file a peer path names, if it is shared with the peer.</summary>
    public async Task<MediaFile?> FindAsync(P2PPeerSnapshot peer, string path, int cueTrack, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var prefixes = await PrefixesAsync(db, peer, cancellationToken);
        foreach (var (sourceId, prefix) in prefixes)
        {
            if (!path.StartsWith(prefix + "/", StringComparison.Ordinal))
                continue;
            var relative = path[(prefix.Length + 1)..];
            return await db.MediaFiles.AsNoTracking().SingleOrDefaultAsync(
                f => f.LibrarySourceId == sourceId && f.RelativePath == relative && f.CueTrack == cueTrack && f.MissingSince == null,
                cancellationToken);
        }

        return null;
    }

    /// <summary>The artwork a peer may see: of a track in its shared libraries.</summary>
    public async Task<Artwork?> ArtworkAsync(P2PPeerSnapshot peer, string hash, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var artwork = await db.Artworks.AsNoTracking().SingleOrDefaultAsync(a => a.Hash == hash, cancellationToken);
        if (artwork is null)
            return null;
        var shared = await db.MediaFiles.AnyAsync(f => peer.LibraryIds.Contains(f.LibrarySource.LibraryId)
                                                       && (f.Track.ArtworkId == artwork.Id || f.Track.Album.ArtworkId == artwork.Id), cancellationToken);
        return shared ? artwork : null;
    }

    /// <summary>The path prefix of each folder of the shared libraries: the library's name, then "-2", "-3"….</summary>
    private static async Task<Dictionary<Guid, string>> PrefixesAsync(ArcanistDbContext db, P2PPeerSnapshot peer, CancellationToken cancellationToken)
    {
        var sources = await db.LibrarySources.AsNoTracking()
            .Where(s => peer.LibraryIds.Contains(s.LibraryId) && s.Kind != LibrarySourceKind.Peer)
            .Select(s => new { s.Id, s.Library.Name, s.Library.Number })
            .ToListAsync(cancellationToken);
        return sources.OrderBy(s => s.Number).ThenBy(s => s.Id).GroupBy(s => s.Name)
            .SelectMany(g => g.Select((s, i) => (s.Id, Prefix: i == 0 ? Clean(g.Key) : $"{Clean(g.Key)}-{i + 1}")))
            .ToDictionary(s => s.Id, s => s.Prefix);

        static string Clean(string name) => name.Replace('/', '-').Trim();
    }

    private static PeerFile Describe(MediaFile file, string prefix, Lyrics? lyrics)
    {
        var track = file.Track;
        var artists = track.Artists.Where(a => a.Role == ArtistRole.Primary).Select(a => a.Artist).ToList();
        string[] Named(ArtistRole role) => track.Artists.Where(a => a.Role == role).Select(a => a.Artist.Name).ToArray();
        var metadata = new AudioMetadata
        {
            Title = track.Title,
            Artists = artists.Select(a => a.Name).ToArray(),
            FeaturedArtists = Named(ArtistRole.Featured),
            Composers = Named(ArtistRole.Composer),
            Lyricists = Named(ArtistRole.Lyricist),
            Conductor = Named(ArtistRole.Conductor).FirstOrDefault(),
            AlbumArtist = track.Album.AlbumArtist.Name,
            SortAlbumArtist = track.Album.AlbumArtist.SortName,
            SortArtist = artists.FirstOrDefault()?.SortName,
            Album = track.Album.Title,
            Genres = track.Genres.Select(g => g.Genre.Name).ToArray(),
            Year = track.Album.Year,
            TrackNumber = track.TrackNumber,
            DiscNumber = track.DiscNumber,
            IsCompilation = track.Album.IsCompilation,
            RecordingMbid = track.Recording.MusicBrainzId,
            ReleaseMbid = track.Album.MusicBrainzReleaseId,
            ArtistMbid = artists.FirstOrDefault()?.MusicBrainzId,
            AlbumArtistMbid = track.Album.AlbumArtist.MusicBrainzId,
            ReplayGainTrackGain = file.ReplayGainTrackGain,
            ReplayGainTrackPeak = file.ReplayGainTrackPeak,
            ReplayGainAlbumGain = file.ReplayGainAlbumGain,
            ReplayGainAlbumPeak = file.ReplayGainAlbumPeak,
            Format = file.Format,
            BitRate = file.BitRate,
            SampleRate = file.SampleRate,
            BitDepth = file.BitDepth,
            Channels = file.Channels,
            DurationMs = file.DurationMs,
            Lyrics = lyrics?.Plain,
            SyncedLyrics = lyrics?.Synced,
            LyricsSource = lyrics?.Source,
        };
        var artwork = track.Artwork?.Hash ?? track.Album.Artwork?.Hash;
        return new PeerFile(file.Id, $"{prefix}/{file.RelativePath}", file.CueTrack, file.StartMs, file.EndMs, file.Size, file.ModifiedAt,
            file.ContentHash, artwork, metadata);
    }
}
