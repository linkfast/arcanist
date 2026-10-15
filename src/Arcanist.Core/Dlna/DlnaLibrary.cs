using System.Text;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Discovery;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using AlbumEntity = Arcanist.Core.Data.Entities.Album;
using Track = Arcanist.Core.Application.Catalog.Track;

namespace Arcanist.Core.Dlna;

/// <summary>
/// The ContentDirectory tree: the sections the administrator chose, over the published libraries. Object ids say
/// what they are (<c>album/&lt;id&gt;</c>, <c>folder/&lt;source&gt;/&lt;path&gt;</c>…); an item's id is its container's
/// id, <c>|</c> and its track, so the same track can sit in several containers.
/// </summary>
internal sealed class DlnaLibrary(
    ArcanistDbContext db,
    CatalogService catalog,
    PlaylistService playlists,
    DiscoveryService discovery,
    UserAccess access,
    DlnaConfig config,
    IReadOnlySet<DlnaFormat> convertible,
    string baseUrl,
    bool spanish)
{
    public const string Root = "0";

    private IQueryable<AlbumEntity> Albums => catalog.VisibleAlbums(access);

    /// <summary>The children of a container, a page of them, and how many there are.</summary>
    public async Task<(IReadOnlyList<DidlObject> Children, int Total)> ChildrenAsync(string id, int start, int count, CancellationToken cancellationToken)
    {
        count = count <= 0 ? 500 : Math.Min(count, 500);
        start = Math.Max(0, start);
        var (kind, rest) = Split(id);
        switch (kind)
        {
            case Root:
            {
                var sections = new List<DidlObject>();
                foreach (var section in config.Sections.Distinct())
                    sections.Add(await SectionAsync(section, cancellationToken));
                return (sections.Skip(start).Take(count).ToList(), sections.Count);
            }
            case "artists":
            {
                var artists = db.Artists.Where(a => Albums.Any(al => al.AlbumArtistId == a.Id)).OrderBy(a => a.NormalizedName).ThenBy(a => a.Id);
                var total = await artists.CountAsync(cancellationToken);
                var page = await artists.Skip(start).Take(count).Select(a => new { a.Id, a.Name }).ToListAsync(cancellationToken);
                var ids = page.Select(a => a.Id).ToList();
                var stats = await Albums.Where(a => ids.Contains(a.AlbumArtistId)).GroupBy(a => a.AlbumArtistId)
                    .Select(g => new { ArtistId = g.Key, Count = g.Count(), Artwork = g.Max(a => a.ArtworkId) })
                    .ToDictionaryAsync(g => g.ArtistId, cancellationToken);
                return (page.Select(a => (DidlObject)new DidlContainer($"artist/{a.Id:N}", id, a.Name, "object.container.person.musicArtist",
                    stats.GetValueOrDefault(a.Id)?.Count ?? 0, ArtUrl: Art(stats.GetValueOrDefault(a.Id)?.Artwork))).ToList(), total);
            }
            case "artist":
                if (!await db.Artists.AnyAsync(a => a.Id == ParseId(rest) && Albums.Any(al => al.AlbumArtistId == a.Id), cancellationToken))
                    throw new UpnpException(701, "No such object");
                return await AlbumPageAsync(id, Albums.Where(a => a.AlbumArtistId == ParseId(rest)).OrderBy(a => a.Year).ThenBy(a => a.NormalizedTitle).ThenBy(a => a.Id),
                    start, count, cancellationToken);
            case "albums":
                return await AlbumPageAsync(id, Albums.OrderBy(a => a.NormalizedTitle).ThenBy(a => a.Id), start, count, cancellationToken);
            case "recent":
            {
                var recent = Albums.OrderByDescending(a => a.Tracks.Min(t => t.AddedAt)).ThenBy(a => a.Id).Take(100);
                return await AlbumPageAsync(id, recent, start, count, cancellationToken);
            }
            case "genres":
            {
                var visible = catalog.Visible(access);
                var genres = await db.TrackGenres.Where(tg => visible.Any(t => t.Id == tg.TrackId))
                    .GroupBy(tg => new { tg.GenreId, tg.Genre.Name })
                    .Select(g => new { g.Key.GenreId, g.Key.Name, Albums = g.Select(tg => tg.Track.AlbumId).Distinct().Count() })
                    .ToListAsync(cancellationToken);
                var ordered = genres.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
                return (ordered.Skip(start).Take(count)
                    .Select(g => (DidlObject)new DidlContainer($"genre/{g.GenreId:N}", id, g.Name, "object.container.genre.musicGenre", g.Albums)).ToList(), ordered.Count);
            }
            case "genre":
            {
                var genre = ParseId(rest);
                return await AlbumPageAsync(id, Albums.Where(a => a.Tracks.Any(t => t.Genres.Any(g => g.GenreId == genre)))
                    .OrderBy(a => a.NormalizedTitle).ThenBy(a => a.Id), start, count, cancellationToken);
            }
            case "decades":
            {
                var decades = await Albums.Where(a => a.Year != null).GroupBy(a => a.Year!.Value / 10 * 10)
                    .Select(g => new { Decade = g.Key, Count = g.Count() }).OrderByDescending(g => g.Decade).ToListAsync(cancellationToken);
                return (decades.Skip(start).Take(count).Select(d => (DidlObject)new DidlContainer($"decade/{d.Decade}", id, DecadeTitle(d.Decade),
                    "object.container.storageFolder", d.Count)).ToList(), decades.Count);
            }
            case "decade":
            {
                var decade = int.TryParse(rest, out var year) ? year : throw new UpnpException(701, "No such object");
                return await AlbumPageAsync(id, Albums.Where(a => a.Year >= decade && a.Year < decade + 10)
                    .OrderBy(a => a.Year).ThenBy(a => a.NormalizedTitle).ThenBy(a => a.Id), start, count, cancellationToken);
            }
            case "album":
            {
                if (!await Albums.AnyAsync(a => a.Id == ParseId(rest), cancellationToken))
                    throw new UpnpException(701, "No such object");
                var tracks = (await catalog.TracksAsync(null, ParseId(rest), null, null, null, cancellationToken)).Items;
                return (await ItemsAsync(id, tracks.Skip(start).Take(count).ToList(), cancellationToken), tracks.Count);
            }
            case "playlists":
            {
                var lists = await playlists.ListAsync(cancellationToken);
                return (lists.Skip(start).Take(count).Select(p => (DidlObject)new DidlContainer($"playlist/{p.Id:N}", id, p.Name,
                    "object.container.playlistContainer", p.TrackCount, ArtUrl: Art(p.ArtworkIds.Count > 0 ? p.ArtworkIds[0] : null))).ToList(), lists.Count);
            }
            case "playlist":
            {
                var tracks = new List<Track>();
                string? after = null;
                do
                {
                    var page = await playlists.EntriesAsync(ParseId(rest), after, PlaylistService.MaxPageSize, cancellationToken);
                    tracks.AddRange(page.Items.Select(e => e.Track));
                    after = page.NextCursor;
                } while (after is not null);

                return (await ItemsAsync(id, tracks.Skip(start).Take(count).ToList(), cancellationToken), tracks.Count);
            }
            case "mixes":
            {
                var mixes = (await discovery.MixesAsync(cancellationToken)).Where(m => m.Kind is MixKind.Genre or MixKind.Decade).ToList();
                return (mixes.Skip(start).Take(count).Select(m => (DidlObject)new DidlContainer($"mix/{m.Id}", id, MixTitle(m),
                    "object.container.playlistContainer", DiscoveryService.MixLength, ArtUrl: Art(m.ArtworkIds.Count > 0 ? m.ArtworkIds[0] : null))).ToList(), mixes.Count);
            }
            case "mix":
            {
                var tracks = (await discovery.MixAsync(rest, cancellationToken)).Tracks;
                return (await ItemsAsync(id, tracks.Skip(start).Take(count).ToList(), cancellationToken), tracks.Count);
            }
            case "folders":
            {
                var sources = await FolderRootsAsync(cancellationToken);
                return (sources.Skip(start).Take(count).Cast<DidlObject>().ToList(), sources.Count);
            }
            case "folder":
                return await FolderAsync(id, rest, start, count, cancellationToken);
            default:
                throw new UpnpException(701, "No such object");
        }
    }

    /// <summary>One object, for BrowseMetadata.</summary>
    public async Task<DidlObject> ObjectAsync(string id, CancellationToken cancellationToken)
    {
        if (id == Root)
            return new DidlContainer(Root, "-1", config.Name, "object.container.storageFolder", config.Sections.Distinct().Count());

        var bar = id.LastIndexOf('|');
        if (bar > 0)
        {
            var parent = id[..bar];
            var track = ParseId(id[(bar + 1)..]);
            var tracks = await catalog.VisibleTracksAsync(access, [track], cancellationToken);
            if (!tracks.TryGetValue(track, out var found))
                throw new UpnpException(701, "No such object");
            return (await ItemsAsync(parent, [found], cancellationToken))[0];
        }

        var (kind, rest) = Split(id);
        if (Enum.TryParse<DlnaSection>(SectionOf(kind) ?? "", out var section) && rest.Length == 0)
            return await SectionAsync(section, cancellationToken);

        switch (kind)
        {
            case "album":
            {
                var page = await AlbumPageAsync("albums", Albums.Where(a => a.Id == ParseId(rest)), 0, 1, cancellationToken);
                return page.Children.Count > 0 ? page.Children[0] : throw new UpnpException(701, "No such object");
            }
            case "folder":
            {
                var (sourceId, path) = ParseFolder(rest);
                var name = path.Length > 0 ? path[(path.LastIndexOf('/') + 1)..] : (await FolderRootsAsync(cancellationToken)).FirstOrDefault(f => f.Id == id)?.Title;
                var parent = path.Length == 0 ? "folders" : FolderId(sourceId, path.Contains('/') ? path[..path.LastIndexOf('/')] : "");
                var (_, total) = await FolderAsync(id, rest, 0, 0, cancellationToken);
                return new DidlContainer(id, parent, name ?? throw new UpnpException(701, "No such object"), "object.container.storageFolder", total);
            }
            default:
            {
                var (parent, title) = kind switch
                {
                    "artist" => ("artists", await db.Artists.Where(a => a.Id == ParseId(rest)).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken)),
                    "genre" => ("genres", await db.Genres.Where(g => g.Id == ParseId(rest)).Select(g => g.Name).FirstOrDefaultAsync(cancellationToken)),
                    "decade" => ("decades", int.TryParse(rest, out var decade) ? DecadeTitle(decade) : null),
                    "playlist" => ("playlists", (await playlists.GetAsync(ParseId(rest), cancellationToken)).Name),
                    "mix" => ("mixes", MixTitle((await discovery.MixAsync(rest, cancellationToken)).Mix)),
                    _ => (Root, null),
                };
                if (title is null)
                    throw new UpnpException(701, "No such object");
                var (_, total) = await ChildrenAsync(id, 0, 1, cancellationToken);
                return new DidlContainer(id, parent, title, kind == "artist" ? "object.container.person.musicArtist"
                    : kind == "genre" ? "object.container.genre.musicGenre"
                    : kind is "playlist" or "mix" ? "object.container.playlistContainer"
                    : "object.container.storageFolder", total);
            }
        }
    }

    private async Task<DidlContainer> SectionAsync(DlnaSection section, CancellationToken cancellationToken)
    {
        var id = section switch
        {
            DlnaSection.RecentlyAdded => "recent",
            _ => section.ToString().ToLowerInvariant(),
        };
        var (_, total) = await ChildrenAsync(id, 0, 1, cancellationToken);
        return new DidlContainer(id, Root, SectionTitle(section), "object.container.storageFolder", total);
    }

    private static string? SectionOf(string id) => id switch
    {
        "recent" => nameof(DlnaSection.RecentlyAdded),
        "artists" or "albums" or "genres" or "decades" or "folders" or "playlists" or "mixes" => char.ToUpperInvariant(id[0]) + id[1..],
        _ => null,
    };

    private async Task<(IReadOnlyList<DidlObject> Children, int Total)> AlbumPageAsync(
        string parent, IQueryable<AlbumEntity> albums, int start, int count, CancellationToken cancellationToken)
    {
        var total = await albums.CountAsync(cancellationToken);
        var page = await catalog.ToModelsAsync(await albums.Skip(start).Take(count).AsNoTracking().ToListAsync(cancellationToken), access, cancellationToken);
        return (page.Select(a => (DidlObject)new DidlContainer($"album/{a.Id:N}", parent, a.Title, "object.container.album.musicAlbum",
            a.TrackCount, a.Artist.Name, Art(a.ArtworkId))).ToList(), total);
    }

    /// <summary>The libraries' folders: one per library folder, named after the library.</summary>
    private async Task<List<DidlContainer>> FolderRootsAsync(CancellationToken cancellationToken)
    {
        var ids = config.LibraryIds.ToList();
        var sources = await db.LibrarySources.AsNoTracking().Where(s => ids.Contains(s.LibraryId))
            .OrderBy(s => s.Library.Name).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.LibraryId, Library = s.Library.Name, s.Location })
            .ToListAsync(cancellationToken);
        var several = sources.GroupBy(s => s.LibraryId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var roots = new List<DidlContainer>();
        foreach (var source in sources)
        {
            var title = several.Contains(source.LibraryId)
                ? $"{source.Library} — {Path.GetFileName(Path.TrimEndingDirectorySeparator(source.Location))}"
                : source.Library;
            var (_, total) = await FolderAsync(FolderId(source.Id, ""), $"{source.Id:N}", 0, 0, cancellationToken);
            roots.Add(new DidlContainer(FolderId(source.Id, ""), "folders", title, "object.container.storageFolder", total));
        }

        return roots;
    }

    /// <summary>A file as the folder tree shows it: where it is, and its place among the files of its folder.</summary>
    private sealed record FolderFile(Guid Id, Guid TrackId, string RelativePath, int CueTrack, string Order);

    /// <summary>
    /// A folder of a library folder: its subfolders first, then its audio files, from the catalog. Ultimate Cloud keeps
    /// no folders (its files are named by id), so its tree is built from what Ultimate Cloud says about each song:
    /// album artist, album, then the songs in disc and track order.
    /// </summary>
    private async Task<(IReadOnlyList<DidlObject> Children, int Total)> FolderAsync(string id, string rest, int start, int count, CancellationToken cancellationToken)
    {
        var (sourceId, path) = ParseFolder(rest);
        var prefix = path.Length == 0 ? "" : path + "/";
        var visible = catalog.VisibleFiles(access).AsNoTracking().Where(f => f.LibrarySourceId == sourceId);
        List<FolderFile> files;
        if (await db.LibrarySources.AnyAsync(s => s.Id == sourceId && s.Kind == LibrarySourceKind.UltimateCloud, cancellationToken))
        {
            var described = await visible
                .Select(f => new
                {
                    f.Id,
                    f.TrackId,
                    f.Track.Title,
                    f.Track.DiscNumber,
                    f.Track.TrackNumber,
                    Album = f.Track.Album.Title,
                    f.Track.Album.Year,
                    Artist = f.Track.Album.AlbumArtist.Name,
                })
                .ToListAsync(cancellationToken);
            files = described.Select(f => new FolderFile(
                    f.Id,
                    f.TrackId,
                    $"{Segment(f.Artist)}/{Segment(f.Year is { } year ? $"{f.Album} ({year})" : f.Album)}/{Segment(f.Title)}",
                    0,
                    $"{(f.DiscNumber ?? 0):D3}-{(f.TrackNumber ?? 0):D4}-{f.Title}"))
                .ToList();
        }
        else
        {
            files = (await visible.Where(f => f.RelativePath.StartsWith(prefix))
                    .Select(f => new { f.Id, f.TrackId, f.RelativePath, f.CueTrack })
                    .ToListAsync(cancellationToken))
                .Select(f => new FolderFile(f.Id, f.TrackId, f.RelativePath, f.CueTrack, f.RelativePath))
                .ToList();
        }

        files = files.Where(f => f.RelativePath.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        var folders = files.Select(f => f.RelativePath[prefix.Length..]).Where(r => r.Contains('/'))
            .Select(r => r[..r.IndexOf('/')]).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
        var here = files.Where(f => !f.RelativePath[prefix.Length..].Contains('/'))
            .OrderBy(f => f.Order, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.CueTrack).ToList();
        var total = folders.Count + here.Count;
        if (count <= 0)
            return ([], total);

        var children = new List<DidlObject>();
        foreach (var folder in folders.Skip(start).Take(count))
        {
            var childPath = prefix + folder;
            var childPrefix = childPath + "/";
            var inside = files.Where(f => f.RelativePath.StartsWith(childPrefix, StringComparison.Ordinal))
                .Select(f => f.RelativePath[childPrefix.Length..]).Select(r => r.Contains('/') ? r[..r.IndexOf('/')] : r).Distinct().Count();
            children.Add(new DidlContainer(FolderId(sourceId, childPath), id, folder, "object.container.storageFolder", inside));
        }

        var fileStart = Math.Max(0, start - folders.Count);
        var fileCount = count - children.Count;
        if (fileCount > 0)
        {
            var page = here.Skip(fileStart).Take(fileCount).ToList();
            var tracks = await catalog.VisibleTracksAsync(access, page.Select(f => f.TrackId).Distinct().ToList(), cancellationToken);
            children.AddRange(await ItemsAsync(id, page.Where(f => tracks.ContainsKey(f.TrackId)).Select(f => (tracks[f.TrackId], (Guid?)f.Id)).ToList(),
                cancellationToken));
        }

        return (children, total);
    }

    private Task<IReadOnlyList<DidlItem>> ItemsAsync(string parent, IReadOnlyList<Track> tracks, CancellationToken cancellationToken) =>
        ItemsAsync(parent, tracks.Select(t => (t, (Guid?)null)).ToList(), cancellationToken);

    /// <summary>Items for tracks, each with its best copy (or the given one) offered in every format, lossless first.</summary>
    private async Task<IReadOnlyList<DidlItem>> ItemsAsync(string parent, IReadOnlyList<(Track Track, Guid? FileId)> tracks, CancellationToken cancellationToken)
    {
        var fileIds = tracks.Select(t => t.FileId ?? (t.Track.Files.Count > 0 ? t.Track.Files[0].Id : Guid.Empty)).ToList();
        var files = await db.MediaFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id))
            .Select(f => new { f.Id, f.RelativePath, f.StartMs })
            .ToDictionaryAsync(f => f.Id, cancellationToken);
        var albumIds = tracks.Select(t => t.Track.Album.Id).Distinct().ToList();
        var albums = await db.Albums.AsNoTracking().Where(a => albumIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Year, Artist = a.AlbumArtist.Name })
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var items = new List<DidlItem>();
        for (var i = 0; i < tracks.Count; i++)
        {
            var (track, _) = tracks[i];
            var copy = track.Files.FirstOrDefault(f => f.Id == fileIds[i]);
            if (copy is null || !files.TryGetValue(copy.Id, out var file))
                continue;
            var album = albums.GetValueOrDefault(track.Album.Id);
            var resources = DlnaFormats.Offered(file.RelativePath, file.StartMs is not null, convertible)
                .Select(format => Resource(track, copy, file.RelativePath, format))
                .ToList();
            items.Add(new DidlItem(
                $"{parent}|{track.Id:N}",
                parent,
                track.Title,
                string.Join(", ", track.Artists.Where(a => a.Role == ArtistRole.Primary).Select(a => a.Name)) is { Length: > 0 } artist ? artist : album?.Artist,
                track.Album.Title,
                album?.Artist,
                track.Genres.FirstOrDefault(),
                track.TrackNumber,
                album?.Year,
                Art(track.ArtworkId),
                resources));
        }

        return items;
    }

    private DidlResource Resource(Track track, TrackFile copy, string path, DlnaFormat format)
    {
        var url = $"{baseUrl}{Extensions.Dlna.DlnaExtension.BasePath}/media/{track.Id:N}/{copy.Id:N}/{DlnaFormats.Name(format)}{DlnaFormats.Extension(format, path)}";
        return format switch
        {
            DlnaFormat.Original => new DidlResource(url, DlnaFormats.ProtocolInfo(format, path), track.DurationMs, copy.Size, copy.BitRate,
                copy.SampleRate, copy.Channels, copy.BitDepth),
            DlnaFormat.Lpcm => new DidlResource(url, DlnaFormats.ProtocolInfo(format, path), track.DurationMs, null, 1411, 44100, 2, 16),
            DlnaFormat.Mp3 => new DidlResource(url, DlnaFormats.ProtocolInfo(format, path), track.DurationMs, null, 320, null, 2, null),
            _ => new DidlResource(url, DlnaFormats.ProtocolInfo(format, path), track.DurationMs, null, null, copy.SampleRate, 2, copy.BitDepth),
        };
    }

    private string? Art(Guid? artworkId) =>
        artworkId is { } id ? $"{baseUrl}{Extensions.Dlna.DlnaExtension.BasePath}/art/{id:N}.jpg" : null;

    private static (string Kind, string Remainder) Split(string id)
    {
        var slash = id.IndexOf('/');
        return slash < 0 ? (id, "") : (id[..slash], id[(slash + 1)..]);
    }

    private static Guid ParseId(string value) =>
        Guid.TryParse(value, out var id) ? id : throw new UpnpException(701, "No such object");

    /// <summary>A name as one level of a folder tree: never empty, and without the separator.</summary>
    private static string Segment(string? name) => string.IsNullOrWhiteSpace(name) ? "—" : name.Trim().Replace('/', '∕');

    public static string FolderId(Guid sourceId, string path) =>
        path.Length == 0 ? $"folder/{sourceId:N}" : $"folder/{sourceId:N}/{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(path))}";

    private static (Guid SourceId, string Path) ParseFolder(string rest)
    {
        var slash = rest.IndexOf('/');
        var source = ParseId(slash < 0 ? rest : rest[..slash]);
        try
        {
            var path = slash < 0 ? "" : Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(rest[(slash + 1)..]));
            return path.Split('/').Any(p => p is "." or "..") ? throw new UpnpException(701, "No such object") : (source, path);
        }
        catch (FormatException)
        {
            throw new UpnpException(701, "No such object");
        }
    }

    private string SectionTitle(DlnaSection section) => (section, spanish) switch
    {
        (DlnaSection.Artists, true) => "Artistas",
        (DlnaSection.Albums, true) => "Álbumes",
        (DlnaSection.Genres, true) => "Géneros",
        (DlnaSection.Decades, true) => "Décadas",
        (DlnaSection.Folders, true) => "Carpetas",
        (DlnaSection.Playlists, true) => "Playlists",
        (DlnaSection.RecentlyAdded, true) => "Añadido recientemente",
        (DlnaSection.Mixes, true) => "Mixes",
        (DlnaSection.RecentlyAdded, false) => "Recently added",
        _ => section.ToString(),
    };

    private string DecadeTitle(int decade) => spanish ? $"Años {decade % 100:00}" : $"{decade}s";

    private string MixTitle(Mix mix) => mix.Kind switch
    {
        MixKind.Decade when int.TryParse(mix.Subject, out var decade) => spanish ? $"Mix de los {decade % 100:00}" : $"{decade}s mix",
        _ => spanish ? $"Mix de {mix.Subject}" : $"{mix.Subject} mix",
    };
}
