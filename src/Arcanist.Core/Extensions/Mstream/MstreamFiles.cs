using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>The file explorer, media, album art, transcoding and lyrics routes.</summary>
internal sealed partial class MstreamExtension
{
    /// <summary>
    /// A folder of a vpath: its subfolders and audio files. Built from the catalog, so it lists the files Arcanist
    /// scanned and nothing else of the server's disks.
    /// </summary>
    private static async Task<IResult> FileExplorerAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var vpaths = await library.VpathsAsync();
        var directory = (call.Body.String("directory") ?? "").Trim();
        if (directory == "~" && vpaths.Count == 1)
            directory = vpaths[0].Name;
        if (directory is "" or "/" or "~")
        {
            return MstreamCall.Json(new JsonObject
            {
                ["path"] = "/",
                ["directories"] = new JsonArray(vpaths.Select(v => (JsonNode?)new JsonObject { ["name"] = v.Name }).ToArray()),
                ["files"] = new JsonArray(),
            });
        }

        var (vpath, prefix, files) = await FolderAsync(library, directory);
        var directories = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Name, string Path, MediaFile File)>();
        foreach (var file in files)
        {
            var rest = file.RelativePath[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash >= 0)
                directories.Add(rest[..slash]);
            else
            {
                var name = rest + (file.CueTrack > 0 ? $"@{file.CueTrack}" : "");
                entries.Add((name, $"{vpath.Name}/{prefix}{name}", file));
            }
        }

        if (prefix.Length > 0 && directories.Count == 0 && entries.Count == 0)
            throw MstreamException.NotFound();

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var objects = new Dictionary<Guid, JsonObject>();
        if (call.Body.Bool("pullMetadata") && entries.Count > 0)
        {
            var tracks = await library.Catalog.VisibleTracksAsync(call.Access, entries.Select(e => e.File.TrackId).Distinct().ToList(), call.Cancellation);
            var items = entries.Where(e => tracks.ContainsKey(e.File.TrackId)).Select(e => (tracks[e.File.TrackId], e.File.Id)).ToList();
            objects = await library.ObjectsByFileAsync(items);
        }

        return MstreamCall.Json(new JsonObject
        {
            ["path"] = $"/{vpath.Name}/{prefix}",
            ["directories"] = new JsonArray(directories.Select(d => (JsonNode?)new JsonObject { ["name"] = d }).ToArray()),
            ["files"] = new JsonArray(entries.Select(e =>
            {
                var entry = new JsonObject
                {
                    ["type"] = Path.GetExtension(e.File.RelativePath).TrimStart('.').ToLowerInvariant(),
                    ["name"] = e.Name,
                };
                if (objects.TryGetValue(e.File.Id, out var metadata))
                    entry["metadata"] = metadata;
                return (JsonNode?)entry;
            }).ToArray()),
        });
    }

    /// <summary>Every audio file under a folder, as vpath paths.</summary>
    private static async Task<IResult> RecursiveAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var (vpath, _, files) = await FolderAsync(library, call.Body.Required("directory"));
        return MstreamCall.Json(new JsonArray(files
            .Select(f => $"{vpath.Name}/{f.RelativePath}{(f.CueTrack > 0 ? $"@{f.CueTrack}" : "")}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(p => (JsonNode?)p)
            .ToArray()));
    }

    /// <summary>The vpath of a folder path, the folder's prefix inside it, and the visible files under it.</summary>
    private static async Task<(Vpath Vpath, string Prefix, List<MediaFile> Files)> FolderAsync(MstreamLibrary library, string directory)
    {
        var path = directory.Trim('/');
        var slash = path.IndexOf('/');
        var name = slash < 0 ? path : path[..slash];
        var relative = slash < 0 ? "" : path[(slash + 1)..].Trim('/');
        var vpath = (await library.VpathsAsync()).FirstOrDefault(v => v.Name == name);
        if (vpath is null || relative.Split('/').Any(segment => segment is "." or ".."))
            throw MstreamException.NotFound();

        var prefix = relative.Length == 0 ? "" : relative + "/";
        var files = await (await library.FilesAsync()).AsNoTracking()
            .Where(f => f.LibrarySourceId == vpath.SourceId && f.RelativePath.StartsWith(prefix))
            .ToListAsync(library.Cancellation);
        // The database may compare without case; paths are exact.
        return (vpath, prefix, files.Where(f => f.RelativePath.StartsWith(prefix, StringComparison.Ordinal)).ToList());
    }

    /// <summary>A file as it is, with range support. A cue track is sent converted, as it is part of a file.</summary>
    private static async Task<IResult> MediaAsync(MstreamCall call)
    {
        var file = await new MstreamLibrary(call).ResolveAsync($"{call.Route("vpath")}/{call.Route("path")}")
                   ?? throw MstreamException.NotFound("file not found");
        return await MediaResponses.StreamAsync(call.Http, file.TrackId, file.Id, false, null, null, null, call.Cancellation);
    }

    /// <summary>Converted audio: <c>codec</c> mp3, opus or aac, <c>bitrate</c> 64k to 192k, <c>offset</c> in seconds.</summary>
    private static async Task<IResult> TranscodeAsync(MstreamCall call)
    {
        var file = await new MstreamLibrary(call).ResolveAsync($"{call.Route("vpath")}/{call.Route("path")}")
                   ?? throw MstreamException.NotFound("file not found");
        var available = call.Service<Application.Server.ServerInfoService>().Get().TranscodingFormats;
        if (DefaultCodec(call) is not { } fallback)
            throw new MstreamException(StatusCodes.Status500InternalServerError, "transcoding disabled");

        TranscodeFormat? requested = call.Query("codec")?.ToLowerInvariant() switch
        {
            "mp3" => TranscodeFormat.Mp3,
            "opus" => TranscodeFormat.Opus,
            "aac" => TranscodeFormat.Aac,
            _ => null,
        };
        var format = requested is { } wanted && available.Contains(wanted) ? wanted : fallback;
        var bitRate = call.Query("bitrate") is { } text && int.TryParse(text.TrimEnd('k', 'K'), NumberStyles.None, CultureInfo.InvariantCulture, out var kbps)
                      && kbps is >= 32 and <= 320
            ? kbps
            : 96;
        var offset = double.TryParse(call.Query("offset"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : (double?)null;
        return await MediaResponses.StreamAsync(call.Http, file.TrackId, file.Id, false, format, bitRate, offset, call.Cancellation);
    }

    /// <summary>A cover by the file name given in track and album objects; <c>compress</c> l is 256 px and s 92 px.</summary>
    private static async Task<IResult> AlbumArtAsync(MstreamCall call)
    {
        var name = call.Route("file") ?? "";
        var dot = name.IndexOf('.');
        if (!Guid.TryParseExact(dot > 0 ? name[..dot] : name, "N", out var id))
            throw MstreamException.NotFound();

        int? size = call.Query("compress") switch
        {
            "l" => 256,
            "s" => 92,
            _ => null,
        };
        var file = await call.Service<ArtworkService>().GetAsync(id, size, call.Cancellation);
        call.Http.Response.Headers.CacheControl = "public, max-age=604800";
        return Results.File(file.Path, file.ContentType, entityTag: new EntityTagHeaderValue(file.ETag));
    }

    /// <summary>A song's lyrics, plain and synchronized (as LRC); 404 without them.</summary>
    private static async Task<IResult> LyricsAsync(MstreamCall call)
    {
        var file = await new MstreamLibrary(call).ResolveAsync(call.Query("path")) ?? throw MstreamException.NotFound();
        var lyrics = await call.Service<CatalogService>().LyricsAsync(file.TrackId, call.Cancellation);
        if (lyrics.Instrumental)
            throw MstreamException.NotFound();

        var source = lyrics.Source switch
        {
            LyricsSource.Sidecar => "sidecar",
            LyricsSource.Embedded => "embedded",
            _ => "lrclib",
        };
        var lrc = lyrics.Synced is { } synced
            ? string.Join('\n', synced.Select(l => string.Create(CultureInfo.InvariantCulture, $"[{l.TimeMs / 60000:00}:{l.TimeMs % 60000 / 1000.0:00.00}]") + l.Text))
            : null;
        return MstreamCall.Json(new JsonObject
        {
            ["lyrics"] = Container(lyrics.Plain),
            ["syncedLyrics"] = Container(lrc),
        });

        JsonObject Container(string? data) => new()
        {
            ["default"] = 0,
            ["lyrics"] = data is null
                ? new JsonArray()
                : new JsonArray(new JsonObject { ["lang"] = null, ["source"] = source, ["data"] = data }),
        };
    }
}
