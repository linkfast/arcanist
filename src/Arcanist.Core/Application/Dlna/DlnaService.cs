using System.Net;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Dlna;
using Arcanist.Core.Extensions;
using Arcanist.Core.Media;
using Microsoft.EntityFrameworkCore;
using DlnaExtension = Arcanist.Core.Extensions.Dlna.DlnaExtension;
using Track = Arcanist.Core.Application.Catalog.Track;

namespace Arcanist.Core.Application.Dlna;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>The DLNA media server: whether it runs, what it publishes and how devices see it.</summary>
/// <param name="Enabled">The DLNA extension is on.</param>
/// <param name="Name">The name devices show.</param>
/// <param name="FriendlyName">The name chosen by an administrator; null for the default.</param>
/// <param name="LibraryIds">Libraries published on the local network, to anyone there, without signing in.</param>
/// <param name="Sections">The top-level folders of the tree devices browse, in order.</param>
/// <param name="Addresses">The description URLs devices find through SSDP, one per local network.</param>
/// <param name="ConvertsFormats">ffmpeg is available, so devices that do not play a format get it converted.</param>
/// <param name="RecordPlays">Plays on renderers ("play to") count in their users' statistics; false while paused.</param>
public sealed record DlnaStatus(
    bool Enabled,
    string Name,
    string? FriendlyName,
    IReadOnlyList<Guid> LibraryIds,
    IReadOnlyList<DlnaSection> Sections,
    Guid? RatingsUserId,
    IReadOnlyList<string> Addresses,
    bool ConvertsFormats,
    bool RecordPlays);

/// <param name="RatingsUserId">Whose stars and ratings the StarTracks and rated sections show; the first administrator when null.</param>
public sealed record DlnaSettingsInput(string? FriendlyName, IReadOnlyList<Guid> LibraryIds, IReadOnlyList<DlnaSection> Sections, Guid? RatingsUserId = null);

/// <summary>A device on the local network Arcanist can play to (a DLNA media renderer).</summary>
/// <param name="Manual">Added by an administrator from its address rather than found on the network.</param>
public sealed record DlnaRenderer(string Id, string Name, string? Manufacturer, string? Model, string Address, bool Manual);

public enum DlnaTransportState
{
    Stopped,
    Playing,
    Paused,
    Transitioning,
    NoMedia,
}

/// <summary>What a renderer is doing.</summary>
/// <param name="Track">The track playing from Arcanist's queue, when the caller can see it.</param>
/// <param name="Queue">Arcanist's queue on the renderer (empty when it plays something else).</param>
/// <param name="Volume">0–100; null when the renderer does not say.</param>
/// <param name="StartedByYou">The queue playing on it is the caller's: only they (or an administrator) control it while it plays.</param>
public sealed record DlnaRendererState(
    DlnaRenderer Renderer,
    DlnaTransportState State,
    Track? Track,
    int PositionMs,
    int DurationMs,
    int? Volume,
    IReadOnlyList<Guid> Queue,
    int QueueIndex,
    bool StartedByYou);

/// <param name="TrackIds">The queue, in order (at most 1,000 tracks).</param>
/// <param name="StartIndex">Where to start; the first track when null.</param>
public sealed record PlayOnDlnaRendererInput(string RendererId, IReadOnlyList<Guid> TrackIds, int? StartIndex);

public enum DlnaRendererAction
{
    Play,
    Pause,
    Stop,
    Next,
    Previous,
    /// <summary>To <see cref="ControlDlnaRendererInput.PositionMs"/> in the current track.</summary>
    Seek,
}

public sealed record ControlDlnaRendererInput(string RendererId, DlnaRendererAction Action, int? PositionMs);

public enum DlnaQueueAction
{
    /// <summary><see cref="EditDlnaQueueInput.TrackIds"/> at the end.</summary>
    Append,
    /// <summary><see cref="EditDlnaQueueInput.TrackIds"/> right after the track playing.</summary>
    InsertNext,
    /// <summary>The entry at <see cref="EditDlnaQueueInput.Index"/> (not the one playing).</summary>
    Remove,
    /// <summary>The entry at <see cref="EditDlnaQueueInput.Index"/> to <see cref="EditDlnaQueueInput.ToIndex"/>.</summary>
    Move,
    /// <summary>Plays the entry at <see cref="EditDlnaQueueInput.Index"/>.</summary>
    Jump,
}

/// <param name="TrackIds">Append and InsertNext: tracks the caller can see (the queue holds at most 1,000).</param>
public sealed record EditDlnaQueueInput(string RendererId, DlnaQueueAction Action, IReadOnlyList<Guid>? TrackIds, int? Index, int? ToIndex);

/// <summary>
/// DLNA: the media server's settings (administrators, with a recent password for what it publishes) and "play to"
/// on renderers of the local network (any user, with the tracks they can see; plays count for them).
/// </summary>
public sealed class DlnaService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    ExtensionHost extensions,
    FfmpegLocator ffmpeg,
    IServiceProvider services)
{
    public const int MaxQueue = 1000;
    public const int NameMaxLength = 64;

    private DlnaSettings Settings => services.GetRequiredService<DlnaSettings>();
    private RendererRegistry Registry => services.GetRequiredService<RendererRegistry>();
    private RendererSessions Sessions => services.GetRequiredService<RendererSessions>();
    private DlnaEndpoint Endpoint => services.GetRequiredService<DlnaEndpoint>();

    private bool Enabled => extensions.IsEnabled(DlnaExtension.Id);

    public async Task<DlnaStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var config = await Settings.GetAsync(cancellationToken);
        // Libraries deleted since they were published are not any more.
        var libraryIds = await db.Libraries.Where(l => config.LibraryIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(cancellationToken);
        return new DlnaStatus(Enabled, config.Name, config.FriendlyName, config.LibraryIds.Where(libraryIds.Contains).ToList(), config.Sections,
            await RatingsUserAsync(config, cancellationToken),
            Enabled ? Endpoint.DescriptionUrls() : [], DlnaFormats.Convertible(ffmpeg.Current).Count > 0, config.RecordPlays);
    }

    public async Task<DlnaStatus> SetSettingsAsync(DlnaSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var errors = new ValidationErrors();
        var name = string.IsNullOrWhiteSpace(input.FriendlyName) ? null : input.FriendlyName.Trim();
        if (name is { Length: > NameMaxLength })
            errors.Add("friendlyName", ErrorCodes.InvalidFormat);
        // One deleted meanwhile (in another tab) is just left out.
        var wanted = (input.LibraryIds ?? []).Distinct().ToList();
        var existing = await db.Libraries.Where(l => wanted.Contains(l.Id)).Select(l => l.Id).ToListAsync(cancellationToken);
        var libraries = wanted.Where(existing.Contains).ToList();
        var sections = (input.Sections ?? []).Distinct().ToList();
        if (sections.Any(s => !Enum.IsDefined(s)))
            errors.Add("sections", ErrorCodes.InvalidFormat);
        if (input.RatingsUserId is { } user && !await db.Users.AnyAsync(u => u.Id == user, cancellationToken))
            errors.Add("ratingsUserId", ErrorCodes.NotFound);
        errors.ThrowIfAny();

        var config = await Settings.GetAsync(cancellationToken);
        await Settings.SetAsync(config with { FriendlyName = name, LibraryIds = libraries, Sections = sections, RatingsUserId = input.RatingsUserId },
            cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    /// <summary>
    /// Pauses or resumes counting the plays of "play to" in users' statistics: renderers keep playing either way. For
    /// when Arcanist's plays should not count for a while, such as while it is the source of a migration.
    /// </summary>
    public async Task<DlnaStatus> SetRecordPlaysAsync(bool record, CancellationToken cancellationToken)
    {
        guard.Admin();
        var config = await Settings.GetAsync(cancellationToken);
        if (config.RecordPlays != record)
            await Settings.SetAsync(config with { RecordPlays = record }, cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    public IReadOnlyList<DlnaRenderer> Renderers()
    {
        guard.Authenticated();
        return Enabled ? Registry.List().Select(ToModel).ToList() : [];
    }

    /// <summary>Asks the local network for renderers now and returns the list a few seconds later (those that answered by then).</summary>
    public async Task<IReadOnlyList<DlnaRenderer>> SearchRenderersAsync(CancellationToken cancellationToken)
    {
        guard.Authenticated();
        RequireEnabled();
        services.GetService<SsdpService>()?.SearchNow();
        await Task.Delay(SearchWait, cancellationToken);
        return Renderers();
    }

    /// <summary>How long a search waits for renderers to answer (SSDP's MX is 2 s).</summary>
    internal static TimeSpan SearchWait { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Another user's queue playing on the renderer is theirs to control: anyone else (but an administrator) gets
    /// <see cref="ErrorCodes.RendererInUse"/> until it stops or runs out.
    /// </summary>
    private void RequireControl(UserAccess access, RendererSession? session)
    {
        if (session is { Active: true } && session.UserId != access.UserId && !access.IsAdmin)
            throw new ArcanistException.Conflict(ErrorCodes.RendererInUse);
    }

    public async Task<DlnaRendererState> EditQueueAsync(EditDlnaQueueInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var device = Find(input.RendererId);
        var session = Sessions.Find(device.Id) ?? throw new ArcanistException.Conflict(ErrorCodes.NothingQueued);
        RequireControl(access, session);

        if (input.Action == DlnaQueueAction.Jump)
        {
            if (input.Index is not { } jump || jump < 0 || jump >= session.Queue.Count)
                throw ArcanistException.Field("index", ErrorCodes.InvalidFormat);
            await Sessions.GoToAsync(session, jump, cancellationToken);
            return await RendererAsync(device.Id, cancellationToken);
        }

        var added = input.Action is DlnaQueueAction.Append or DlnaQueueAction.InsertNext
            ? await BuildQueueAsync(access, device, input.TrackIds ?? [], cancellationToken)
            : [];
        string? error = null;
        await Sessions.EditAsync(session, s =>
        {
            switch (input.Action)
            {
                case DlnaQueueAction.Append or DlnaQueueAction.InsertNext:
                    if (s.Queue.Count + added.Count > MaxQueue) { error = "trackIds"; return false; }
                    s.Queue.InsertRange(input.Action == DlnaQueueAction.Append ? s.Queue.Count : Math.Min(s.Index + 1, s.Queue.Count), added);
                    return true;
                case DlnaQueueAction.Remove:
                    if (input.Index is not { } i || i < 0 || i >= s.Queue.Count || i == s.Index) { error = "index"; return false; }
                    s.Queue.RemoveAt(i);
                    if (i < s.Index) s.Index--;
                    return true;
                case DlnaQueueAction.Move:
                    if (input.Index is not { } from || from < 0 || from >= s.Queue.Count) { error = "index"; return false; }
                    if (input.ToIndex is not { } to || to < 0 || to >= s.Queue.Count) { error = "toIndex"; return false; }
                    var entry = s.Queue[from];
                    s.Queue.RemoveAt(from);
                    s.Queue.Insert(to, entry);
                    if (from == s.Index) s.Index = to;
                    else if (from < s.Index && to >= s.Index) s.Index--;
                    else if (from > s.Index && to <= s.Index) s.Index++;
                    return true;
                default:
                    error = "action";
                    return false;
            }
        }, cancellationToken);
        if (error is not null)
            throw ArcanistException.Field(error, error == "trackIds" ? ErrorCodes.TooMany : ErrorCodes.InvalidFormat);
        return await RendererAsync(device.Id, cancellationToken);
    }

    /// <summary>Adds a renderer by the URL of its description, for networks where it is not found by itself.</summary>
    public async Task<DlnaRenderer> AddRendererAsync(string url, CancellationToken cancellationToken)
    {
        guard.Admin();
        RequireEnabled();
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var location) || location.Scheme is not ("http" or "https"))
            throw ArcanistException.Field("url", ErrorCodes.InvalidFormat);
        var device = await Registry.SeenAsync(location, manual: true, cancellationToken)
                     ?? throw ArcanistException.Field("url", ErrorCodes.InvalidFormat);
        return ToModel(device);
    }

    public async Task<DlnaRendererState> RendererAsync(string id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var device = Find(id);
        var client = Registry.Client;
        var status = await RendererControl.StatusAsync(client, device, cancellationToken);
        int? volume;
        try
        {
            volume = await RendererControl.VolumeAsync(client, device, cancellationToken);
        }
        catch (ArcanistException)
        {
            volume = null;
        }

        var session = Sessions.Find(device.Id);
        Track? track = null;
        if (session?.Current is { } current && status.TrackUri == current.Url)
        {
            var visible = await catalog.VisibleTracksAsync(access, [current.TrackId], cancellationToken);
            track = visible.GetValueOrDefault(current.TrackId);
        }

        return new DlnaRendererState(
            ToModel(device),
            status.State switch
            {
                "PLAYING" => DlnaTransportState.Playing,
                "PAUSED_PLAYBACK" or "PAUSED_RECORDING" => DlnaTransportState.Paused,
                "TRANSITIONING" => DlnaTransportState.Transitioning,
                "NO_MEDIA_PRESENT" => DlnaTransportState.NoMedia,
                _ => DlnaTransportState.Stopped,
            },
            track,
            status.PositionMs,
            status.DurationMs > 0 ? status.DurationMs : track?.DurationMs ?? 0,
            volume,
            session?.Queue.Select(e => e.TrackId).ToList() ?? [],
            session?.Index ?? 0,
            session?.UserId == access.UserId);
    }

    /// <summary>
    /// Plays tracks on a renderer, replacing what it played. Each track goes in the best format the renderer says it
    /// plays: the original when it is lossless, then FLAC, LPCM and MP3.
    /// </summary>
    public async Task<DlnaRendererState> PlayAsync(PlayOnDlnaRendererInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var device = Find(input.RendererId);
        RequireControl(access, Sessions.Find(device.Id));
        var queue = await BuildQueueAsync(access, device, input.TrackIds ?? [], cancellationToken);
        await Sessions.StartAsync(device, access.UserId, queue, input.StartIndex ?? 0, cancellationToken);
        return await RendererAsync(device.Id, cancellationToken);
    }

    /// <summary>Queue entries for the tracks the caller can see, each in the best format the renderer plays.</summary>
    private async Task<List<QueueEntry>> BuildQueueAsync(UserAccess access, RendererDevice device, IReadOnlyList<Guid> trackIds, CancellationToken cancellationToken)
    {
        var ids = trackIds.Take(MaxQueue + 1).ToList();
        if (ids.Count > MaxQueue)
            throw ArcanistException.Field("trackIds", ErrorCodes.TooMany);
        var visible = await catalog.VisibleTracksAsync(access, ids.Distinct().ToList(), cancellationToken);
        var tracks = ids.Where(visible.ContainsKey).Select(id => visible[id]).Where(t => t.Files.Count > 0).ToList();
        if (tracks.Count == 0)
            throw ArcanistException.Field("trackIds", ErrorCodes.Required);

        var host = IPAddress.TryParse(device.Location.Host, out var address) ? address : null;
        var baseUrl = Endpoint.BaseFor(host) ?? throw new ArcanistException.Conflict(ErrorCodes.DlnaDisabled);
        var accepts = await RendererControl.AcceptsAsync(Registry.Client, device, cancellationToken);
        var convertible = DlnaFormats.Convertible(ffmpeg.Current);
        var fileIds = tracks.Select(t => t.Files[0].Id).ToList();
        var files = await db.MediaFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id))
            .Select(f => new { f.Id, f.RelativePath, f.StartMs }).ToDictionaryAsync(f => f.Id, cancellationToken);
        var albumIds = tracks.Select(t => t.Album.Id).Distinct().ToList();
        var albums = await db.Albums.AsNoTracking().Where(a => albumIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Year, Artist = a.AlbumArtist.Name }).ToDictionaryAsync(a => a.Id, cancellationToken);
        var tokens = services.GetRequiredService<DlnaPlayTokens>();

        var queue = new List<QueueEntry>();
        foreach (var track in tracks)
        {
            var copy = track.Files[0];
            var file = files[copy.Id];
            var segment = file.StartMs is not null;
            var format = DlnaFormats.Choose(file.RelativePath, segment, convertible, accepts)
                         ?? (convertible.Contains(DlnaFormat.Mp3) ? DlnaFormat.Mp3 : DlnaFormat.Original);
            var token = tokens.Create(new DlnaPlayGrant(access.UserId, track.Id, copy.Id, format));
            var url = $"{baseUrl}{DlnaExtension.BasePath}/play/{token}/track{DlnaFormats.Extension(format, file.RelativePath)}";
            var album = albums.GetValueOrDefault(track.Album.Id);
            var artist = string.Join(", ", track.Artists.Where(a => a.Role == ArtistRole.Primary).Select(a => a.Name));
            var metadata = Didl.Write(
            [
                new DidlItem($"arcanist|{track.Id:N}", "arcanist", track.Title, artist.Length > 0 ? artist : album?.Artist, track.Album.Title,
                    album?.Artist, track.Genres.FirstOrDefault(), track.TrackNumber, album?.Year,
                    track.ArtworkId is { } art ? $"{baseUrl}{DlnaExtension.BasePath}/art/{art:N}.jpg" : null,
                    [new DidlResource(url, DlnaFormats.ProtocolInfo(format, file.RelativePath), track.DurationMs, format == DlnaFormat.Original ? copy.Size : null,
                        null, null, null, null)]),
            ]);
            queue.Add(new QueueEntry(track.Id, url, metadata, track.DurationMs));
        }
        return queue;
    }

    public async Task<DlnaRendererState> ControlAsync(ControlDlnaRendererInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var device = Find(input.RendererId);
        var client = Registry.Client;
        var session = Sessions.Find(device.Id);
        RequireControl(access, session);
        switch (input.Action)
        {
            case DlnaRendererAction.Play:
                if (session is not null)
                    session.Stopped = false;
                await RendererControl.PlayAsync(client, device, cancellationToken);
                break;
            case DlnaRendererAction.Pause:
                await RendererControl.PauseAsync(client, device, cancellationToken);
                break;
            case DlnaRendererAction.Stop:
                if (session is not null)
                    session.Stopped = true;
                await RendererControl.StopAsync(client, device, cancellationToken);
                break;
            case DlnaRendererAction.Next or DlnaRendererAction.Previous:
                if (session is null)
                    throw new ArcanistException.Conflict(ErrorCodes.NothingQueued);
                await Sessions.GoToAsync(session, session.Index + (input.Action == DlnaRendererAction.Next ? 1 : -1), cancellationToken);
                break;
            case DlnaRendererAction.Seek:
                await RendererControl.SeekAsync(client, device, input.PositionMs ?? throw ArcanistException.Field("positionMs", ErrorCodes.Required),
                    cancellationToken);
                break;
            default:
                throw ArcanistException.Field("action", ErrorCodes.InvalidFormat);
        }

        return await RendererAsync(device.Id, cancellationToken);
    }

    public async Task<DlnaRendererState> SetVolumeAsync(string rendererId, int volume, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (volume is < 0 or > 100)
            throw ArcanistException.Field("volume", ErrorCodes.InvalidFormat);
        var device = Find(rendererId);
        RequireControl(access, Sessions.Find(device.Id));
        await RendererControl.SetVolumeAsync(Registry.Client, device, volume, cancellationToken);
        return await RendererAsync(device.Id, cancellationToken);
    }

    private RendererDevice Find(string id)
    {
        RequireEnabled();
        return Registry.Find(id ?? "") ?? throw new ArcanistException.NotFound();
    }

    /// <summary>Whose stars and ratings DLNA shows: the chosen user while they exist, or else the first administrator.</summary>
    internal static async Task<Guid?> RatingsUserAsync(ArcanistDbContext db, DlnaConfig config, CancellationToken cancellationToken)
    {
        if (config.RatingsUserId is { } chosen && await db.Users.AnyAsync(u => u.Id == chosen, cancellationToken))
            return chosen;
        return await db.Users.Where(u => u.Role == UserRole.Admin && !u.IsDisabled).OrderBy(u => u.CreatedAt)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private Task<Guid?> RatingsUserAsync(DlnaConfig config, CancellationToken cancellationToken) => RatingsUserAsync(db, config, cancellationToken);

    private void RequireEnabled()
    {
        if (!Enabled)
            throw new ArcanistException.Conflict(ErrorCodes.DlnaDisabled);
    }

    private static DlnaRenderer ToModel(RendererDevice device) =>
        new(device.Id, device.Name, device.Manufacturer, device.Model, device.Location.Host, device.Manual);
}
