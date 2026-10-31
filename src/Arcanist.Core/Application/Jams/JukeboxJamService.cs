using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Application.Sharing;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Jams;

/// <summary>
/// Jukebox Jams: a live queue several people add songs to while it plays on one player of the host's (a client that
/// implements the host API, the server's audio or a DLNA renderer). Users of the server take part with their account;
/// with the server's setting and the host's permission (SHARE_LINKS), guests without one too, by name, with a limit of
/// songs. Songs are those the host can listen to, from the Jam's libraries. Voting Jams play the most voted first; a
/// DJ (Ultimate's AI DJ, in Ultimate Desktop) may choose the order itself, and everyone is told.
/// </summary>
public sealed class JukeboxJamService(
    ArcanistDbContext db,
    AccessGuard guard,
    ICurrentUser currentUser,
    CatalogService catalog,
    PlaylistService playlists,
    SharingSettings sharing,
    SharingLinks links,
    UserAccessCache users,
    JukeboxJamSignals signals,
    TimeProvider time)
{
    public const int VoteIntervalSeconds = 30;
    public static readonly TimeSpan GuestWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan HostOnline = TimeSpan.FromMinutes(1);
    public const int MaxAddsPerCall = 50;
    public const int MaxWaiting = 1000;
    public const int GuestNameMaxLength = 40;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Who is asking: a user (the host, a member or not yet), a guest, or a visitor with the link.</summary>
    private sealed record Caller(UserAccess? User, JamGuest? Guest, JukeboxJamParticipation Participation)
    {
        public bool CanControl => Participation is JukeboxJamParticipation.Host or JukeboxJamParticipation.Manager;
    }

    /// <summary>The Jams the user hosts or takes part in that have not ended.</summary>
    public async Task<IReadOnlyList<JukeboxJamSummary>> MineAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var jams = await db.JamSessions.AsNoTracking().Include(j => j.Playlist)
            .Where(j => j.EndedAt == null && (j.HostUserId == access.UserId || j.Playlist.Collaborators.Any(c => c.UserId == access.UserId)))
            .OrderByDescending(j => j.StartedAt).ToListAsync(cancellationToken);
        var ids = jams.Select(j => j.PlaylistId).ToList();
        var waiting = await db.PlaylistEntries.Where(e => ids.Contains(e.PlaylistId) && e.PlayedAt == null)
            .GroupBy(e => e.PlaylistId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var hosts = await playlists.UsersAsync(jams.Select(j => j.HostUserId).Distinct().ToList(), cancellationToken);
        return jams.Select(j => new JukeboxJamSummary(j.Code, j.Playlist.Name, hosts.GetValueOrDefault(j.HostUserId)?.Name ?? "",
            j.HostUserId == access.UserId, ToModel(j.Target), waiting.GetValueOrDefault(j.PlaylistId), j.StartedAt)).ToList();
    }

    /// <summary>Starts a Jam the user hosts, with a playlist of its own for its queue.</summary>
    public async Task<JukeboxJam> StartAsync(StartJukeboxJamInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var target = input.PlaysOn switch
        {
            JukeboxJamPlaysOn.Server => JamTarget.Server,
            JukeboxJamPlaysOn.Dlna => JamTarget.Dlna,
            _ => JamTarget.Client,
        };
        var rendererId = target switch
        {
            JamTarget.Server => ServerAudio.ServerAudioService.RendererId,
            JamTarget.Dlna => string.IsNullOrWhiteSpace(input.RendererId) ? throw ArcanistException.Field("rendererId", ErrorCodes.Required) : input.RendererId.Trim(),
            _ => null,
        };
        if (rendererId == ServerAudio.ServerAudioService.RendererId && !access.Has(Permissions.PlayOnServer))
            throw new ArcanistException.Forbidden();
        if (input.AllowGuests)
            await RequireGuestsAsync(access, cancellationToken);
        var libraries = await LibrariesAsync(access, input.GuestLibraryIds, cancellationToken);

        var now = Now();
        var playlist = await playlists.NewAsync(access, new CreatePlaylistInput(input.Name, null, false, []), PlaylistKind.Jam, cancellationToken);
        var jam = new JamSession
        {
            PlaylistId = playlist.Id,
            HostUserId = access.UserId,
            Code = SharingLinks.NewToken(),
            Target = target,
            RendererId = rendererId,
            Voting = input.Voting,
            AllowGuests = input.AllowGuests,
            GuestLibraryIds = libraries is null ? null : JsonSerializer.Serialize(libraries, Json),
            StartedAt = now,
            LastActivityAt = now,
        };
        db.JamSessions.Add(jam);
        await db.SaveChangesAsync(cancellationToken);
        playlists.Publish(playlist, []);
        Publish(jam, JukeboxJamChangeKind.Settings);
        return await GetAsync(jam.Code, null, cancellationToken);
    }

    /// <summary>A Jam by its code, as the one asking sees it (an ended one too, to say so).</summary>
    public async Task<JukeboxJam> GetAsync(string code, string? guestToken, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, guestToken, cancellationToken);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>
    /// Takes part. A signed-in user becomes a member of its playlist (they can add songs); someone without an account
    /// gets a guest session by the name they give, when the Jam takes guests.
    /// </summary>
    public async Task<JukeboxJamGuestSession?> JoinAsync(string code, string? name, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireOpen(jam);
        if (caller.User is { } user)
        {
            await EnsureMemberAsync(jam, user, cancellationToken);
            return null;
        }

        await RequireGuestsOpenAsync(jam, cancellationToken);
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw ArcanistException.Field("name", ErrorCodes.Required);
        if (trimmed.Length > GuestNameMaxLength)
            throw ArcanistException.Field("name", ErrorCodes.InvalidFormat);

        using (await signals.LockAsync(jam.Id, cancellationToken))
        {
            var config = await sharing.GetAsync(cancellationToken);
            if (await db.JamGuests.CountAsync(g => g.JamId == jam.Id && !g.Banned, cancellationToken) >= config.MaxGuests)
                throw new ArcanistException.Conflict(ErrorCodes.JamFull);
            var token = SharingLinks.NewToken() + SharingLinks.NewToken();
            var guest = new JamGuest { JamId = jam.Id, Name = trimmed, TokenHash = Hash(token), JoinedAt = Now(), LastSeenAt = Now() };
            db.JamGuests.Add(guest);
            jam.LastActivityAt = Now();
            await db.SaveChangesAsync(cancellationToken);
            Publish(jam, JukeboxJamChangeKind.Guests);
            return new JukeboxJamGuestSession(token, guest.Id, guest.Name,
                await ToModelAsync(jam, new Caller(null, guest, JukeboxJamParticipation.Guest), cancellationToken));
        }
    }

    /// <summary>Songs that can be added (from the Jam's libraries, among those the host can listen to).</summary>
    public async Task<IReadOnlyList<JukeboxJamSearchResult>> SearchAsync(string code, string query, int? first, string? guestToken,
        CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, guestToken, cancellationToken);
        RequireOpen(jam);
        RequireParticipant(caller);
        var hits = FullTextSearch.Match(db, SearchKind.Tracks, query);
        if (hits is null)
            return [];

        var host = await HostAsync(jam, cancellationToken);
        var size = Math.Clamp(first ?? 30, 1, 100);
        var ids = await Allowed(jam, host).Where(t => hits.Any(h => h.Id == t.Id))
            .OrderByDescending(t => hits.Where(h => h.Id == t.Id).Select(h => h.Score).FirstOrDefault())
            .Select(t => t.Id).Take(size).ToListAsync(cancellationToken);
        var songs = await SongsAsync(host, ids, cancellationToken);
        var queued = await db.PlaylistEntries.Where(e => e.PlaylistId == jam.PlaylistId && e.PlayedAt == null && ids.Contains(e.TrackId))
            .Select(e => e.TrackId).ToListAsync(cancellationToken);
        return ids.Where(songs.ContainsKey).Select(id => new JukeboxJamSearchResult(songs[id], queued.Contains(id))).ToList();
    }

    /// <summary>
    /// Adds songs to the queue. Guests within their limit; in a voting Jam each comes with a vote of whoever adds it
    /// (that does not make them wait). <see cref="JukeboxJamAddSource.Dj"/> is the host's DJ: no limit and no vote.
    /// </summary>
    public async Task<JukeboxJam> AddAsync(string code, IReadOnlyList<Guid> trackIds, JukeboxJamAddSource? source, string? guestToken,
        CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, guestToken, cancellationToken);
        RequireOpen(jam);
        RequireParticipant(caller);
        var dj = source == JukeboxJamAddSource.Dj;
        if (dj && caller.Participation != JukeboxJamParticipation.Host)
            throw new ArcanistException.Forbidden(ErrorCodes.JamNotHost);
        if (trackIds is not { Count: > 0 })
            throw ArcanistException.Field("trackIds", ErrorCodes.Required);
        if (trackIds.Count > MaxAddsPerCall)
            throw ArcanistException.Field("trackIds", ErrorCodes.InvalidFormat);

        var host = await HostAsync(jam, cancellationToken);
        var distinct = trackIds.Distinct().ToList();
        var allowed = await Allowed(jam, host).Where(t => distinct.Contains(t.Id)).CountAsync(cancellationToken);
        if (allowed != distinct.Count)
            throw ArcanistException.Field("trackIds", ErrorCodes.JamTrackNotAllowed);
        if (caller.User is { } user)
            await EnsureMemberAsync(jam, user, cancellationToken);

        using (await signals.LockAsync(jam.Id, cancellationToken))
        {
            var now = Now();
            if (caller.Guest is { } guest && !dj)
            {
                var limit = (await sharing.GetAsync(cancellationToken)).GuestAddLimit;
                var recent = await db.PlaylistEntries.Where(e => e.PlaylistId == jam.PlaylistId && e.AddedByGuestId == guest.Id && e.AddedAt > now - GuestWindow)
                    .OrderBy(e => e.AddedAt).Select(e => e.AddedAt).ToListAsync(cancellationToken);
                if (recent.Count + trackIds.Count > limit)
                {
                    var wait = recent.Count == 0 ? GuestWindow : recent[Math.Max(0, recent.Count + trackIds.Count - limit - 1)] + GuestWindow - now;
                    throw new ArcanistException.TooSoon(ErrorCodes.JamGuestLimit, Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)));
                }
            }

            var count = await db.PlaylistEntries.CountAsync(e => e.PlaylistId == jam.PlaylistId, cancellationToken);
            var waiting = await db.PlaylistEntries.CountAsync(e => e.PlaylistId == jam.PlaylistId && e.PlayedAt == null, cancellationToken);
            if (waiting + trackIds.Count > MaxWaiting || count + trackIds.Count > PlaylistService.MaxEntries)
                throw ArcanistException.Field("trackIds", ErrorCodes.PlaylistFull);

            var entries = trackIds.Select((trackId, i) => new Data.Entities.PlaylistEntry
            {
                PlaylistId = jam.PlaylistId,
                TrackId = trackId,
                Position = count + i,
                AddedAt = now,
                AddedByUserId = caller.User?.UserId,
                AddedByGuestId = caller.Guest?.Id,
                AddedByDj = dj,
            }).ToList();
            db.PlaylistEntries.AddRange(entries);
            if (jam.Voting && !dj)
                db.JamVotes.AddRange(entries.Select(e => new JamVote
                {
                    JamId = jam.Id, EntryId = e.Id, UserId = caller.User?.UserId, GuestId = caller.Guest?.Id, CastAt = now, FromAdding = true,
                }));
            jam.LastActivityAt = now;
            jam.Playlist.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        Changed(jam, JukeboxJamChangeKind.Queue);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>
    /// A vote for a waiting song (voting Jams). Anyone taking part votes as often as they like, but once every
    /// <see cref="VoteIntervalSeconds"/> seconds in the whole Jam: sooner is <c>jam_vote_too_soon</c> with the seconds left.
    /// </summary>
    public async Task<JukeboxJam> VoteAsync(string code, Guid entryId, string? guestToken, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, guestToken, cancellationToken);
        RequireOpen(jam);
        RequireParticipant(caller);
        if (!jam.Voting)
            throw new ArcanistException.Conflict(ErrorCodes.JamVotingOff);
        if (caller.User is { } user)
            await EnsureMemberAsync(jam, user, cancellationToken);

        using (await signals.LockAsync(jam.Id, cancellationToken))
        {
            if (!await db.PlaylistEntries.AnyAsync(e => e.Id == entryId && e.PlaylistId == jam.PlaylistId && e.PlayedAt == null, cancellationToken)
                || jam.CurrentEntryId == entryId)
                throw ArcanistException.Field("entryId", ErrorCodes.NotFound);

            var now = Now();
            if (await NextVoteAtAsync(jam, caller, cancellationToken) is { } next && next > now)
                throw new ArcanistException.TooSoon(ErrorCodes.JamVoteTooSoon, Math.Max(1, (int)Math.Ceiling((next - now).TotalSeconds)));

            db.JamVotes.Add(new JamVote { JamId = jam.Id, EntryId = entryId, UserId = caller.User?.UserId, GuestId = caller.Guest?.Id, CastAt = now });
            jam.LastActivityAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        Changed(jam, JukeboxJamChangeKind.Queue);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>Controls the queue and what plays: the host and those who manage its playlist.</summary>
    public async Task<JukeboxJam> ControlAsync(string code, ControlJukeboxJamInput input, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireOpen(jam);
        if (!caller.CanControl)
            throw new ArcanistException.Forbidden();

        switch (input.Action)
        {
            case JukeboxJamAction.Skip or JukeboxJamAction.Pause or JukeboxJamAction.Resume:
                // The playing client (or JukeboxJamPlayer, for the server's audio and renderers) does it.
                if (input.Action != JukeboxJamAction.Skip)
                    jam.Paused = input.Action == JukeboxJamAction.Pause;
                await db.SaveChangesAsync(cancellationToken);
                signals.Publish(jam.Id, new JukeboxJamChange(jam.Code, JukeboxJamChangeKind.Command, input.Action, jam.HostDeviceId, Now()));
                break;

            case JukeboxJamAction.Remove:
            {
                var entry = await WaitingEntryAsync(jam, input.EntryId, cancellationToken);
                db.PlaylistEntries.Remove(entry);
                if (jam.PinnedEntryId == entry.Id)
                    jam.PinnedEntryId = null;
                await db.JamVotes.Where(v => v.JamId == jam.Id && v.EntryId == entry.Id).ExecuteDeleteAsync(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await RenumberAsync(jam, cancellationToken);
                Changed(jam, JukeboxJamChangeKind.Queue);
                break;
            }

            case JukeboxJamAction.Pin:
                jam.PinnedEntryId = (await WaitingEntryAsync(jam, input.EntryId, cancellationToken)).Id;
                await db.SaveChangesAsync(cancellationToken);
                Changed(jam, JukeboxJamChangeKind.Queue);
                break;

            case JukeboxJamAction.Unpin:
                jam.PinnedEntryId = null;
                await db.SaveChangesAsync(cancellationToken);
                Changed(jam, JukeboxJamChangeKind.Queue);
                break;

            case JukeboxJamAction.Move:
            {
                var entry = await WaitingEntryAsync(jam, input.EntryId, cancellationToken);
                var waiting = (await WaitingAsync(jam, cancellationToken)).Select(e => e.Id).Where(id => id != entry.Id).ToList();
                waiting.Insert(Math.Clamp(input.Position ?? 0, 0, waiting.Count), entry.Id);
                // Waiting songs keep their places after the played ones, in the new order.
                var all = await db.PlaylistEntries.Where(e => e.PlaylistId == jam.PlaylistId).OrderBy(e => e.Position).ToListAsync(cancellationToken);
                var ordered = all.Where(e => !waiting.Contains(e.Id)).Concat(waiting.Select(id => all.First(e => e.Id == id))).ToList();
                for (var i = 0; i < ordered.Count; i++)
                    ordered[i].Position = i;
                await db.SaveChangesAsync(cancellationToken);
                Changed(jam, JukeboxJamChangeKind.Queue);
                break;
            }
        }

        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>Changes the Jam's name, voting, guests and libraries. Host only.</summary>
    public async Task<JukeboxJam> UpdateAsync(string code, UpdateJukeboxJamInput input, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireOpen(jam);
        var access = RequireHost(jam, caller);
        if (input.Name is { } name)
        {
            var trimmed = name.Trim();
            if (trimmed.Length is 0 or > PlaylistService.NameMaxLength)
                throw ArcanistException.Field("name", ErrorCodes.InvalidFormat);
            jam.Playlist.Name = trimmed;
            jam.Playlist.NormalizedName = TextNormalizer.Normalize(trimmed);
            jam.Playlist.SearchText = FullTextSearch.Text(trimmed, jam.Playlist.Description);
        }
        if (input.Voting is { } voting)
            jam.Voting = voting;
        if (input.AllowGuests is { } allow)
        {
            if (allow && !jam.AllowGuests)
                await RequireGuestsAsync(access, cancellationToken);
            jam.AllowGuests = allow;
        }
        if (input.GuestLibraryIds is not null)
        {
            var libraries = await LibrariesAsync(access, input.GuestLibraryIds, cancellationToken);
            jam.GuestLibraryIds = libraries is null ? null : JsonSerializer.Serialize(libraries, Json);
        }
        jam.Playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        Publish(jam, JukeboxJamChangeKind.Settings);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>Takes a guest out: their session no longer works. The host and those who manage it.</summary>
    public async Task<JukeboxJam> RemoveGuestAsync(string code, Guid guestId, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        if (!caller.CanControl)
            throw new ArcanistException.Forbidden();
        var guest = await db.JamGuests.SingleOrDefaultAsync(g => g.Id == guestId && g.JamId == jam.Id, cancellationToken)
                    ?? throw new ArcanistException.NotFound();
        guest.Banned = true;
        await db.SaveChangesAsync(cancellationToken);
        Publish(jam, JukeboxJamChangeKind.Guests);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>Ends the Jam: its code no longer works and its queue stays as a normal playlist of the host's. Host only.</summary>
    public async Task<JukeboxJam> EndAsync(string code, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireHost(jam, caller);
        if (jam.EndedAt is null)
        {
            await EndAsync(db, jam, Now(), cancellationToken);
            Publish(jam, JukeboxJamChangeKind.Ended);
        }
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>Ends a Jam (also the player's, after hours without activity): what waits is dropped, the rest stays.</summary>
    internal static async Task EndAsync(ArcanistDbContext db, JamSession jam, DateTime now, CancellationToken cancellationToken)
    {
        jam.EndedAt = now;
        jam.Playlist.Kind = PlaylistKind.Normal;
        jam.Playlist.UpdatedAt = now;
        jam.CurrentEntryId = null;
        jam.DjKind = JamDj.None;
        await db.PlaylistEntries.Where(e => e.PlaylistId == jam.PlaylistId && e.PlayedAt == null).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var positions = await db.PlaylistEntries.Where(e => e.PlaylistId == jam.PlaylistId).OrderBy(e => e.Position).ToListAsync(cancellationToken);
        for (var i = 0; i < positions.Count; i++)
            positions[i].Position = i;
        await db.SaveChangesAsync(cancellationToken);
    }

    // —— The host API: the client of the host's that plays the Jam. ——

    /// <summary>
    /// The host's client that plays the Jam (a web player tab, Ultimate Desktop): another one claiming it takes over,
    /// and the one before is told (<see cref="JukeboxJamChangeKind.Host"/> with the new device).
    /// </summary>
    public async Task<JukeboxJam> ClaimHostAsync(string code, ClaimJukeboxJamHostInput input, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireOpen(jam);
        RequireHost(jam, caller);
        if (jam.Target != JamTarget.Client)
            throw new ArcanistException.Conflict(ErrorCodes.JamNotHost);
        if (string.IsNullOrWhiteSpace(input.DeviceId) || input.DeviceId.Length > 200)
            throw ArcanistException.Field("deviceId", ErrorCodes.InvalidFormat);

        jam.HostDeviceId = input.DeviceId;
        jam.HostClient = JsonSerializer.Serialize(new StoredClient(input.Kind, Cut(input.PlayerId, 100), Cut(input.Name, 100), Cut(input.Version, 50)), Json);
        jam.HostSeenAt = Now();
        jam.LastActivityAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        Publish(jam, JukeboxJamChangeKind.Host);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>The song the host's client should play next (the server's order: pinned, then votes, then arrival), with its stream.</summary>
    public async Task<JukeboxJamNext?> NextAsync(string code, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireOpen(jam);
        var access = RequireHost(jam, caller);
        foreach (var entry in await WaitingAsync(jam, cancellationToken))
        {
            if (entry.Id == jam.CurrentEntryId)
                continue;
            if ((await catalog.VisibleTracksAsync(access, [entry.TrackId], cancellationToken)).GetValueOrDefault(entry.TrackId) is { } track)
                return new JukeboxJamNext(entry.Id, track);
        }
        return null;
    }

    /// <summary>
    /// What the host's client plays: an entry of the Jam (it leaves the queue; any waiting one, the DJ chooses), a song
    /// not on the server (<see cref="ReportJukeboxJamPlaybackInput.External"/>) or nothing; paused or not, and where.
    /// Every 5 seconds and on every change; only from the device that claimed it.
    /// </summary>
    public async Task<JukeboxJam> ReportAsync(string code, ReportJukeboxJamPlaybackInput input, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireOpen(jam);
        RequireHost(jam, caller);
        if (jam.Target != JamTarget.Client || jam.HostDeviceId != input.DeviceId)
            throw new ArcanistException.Conflict(ErrorCodes.JamNotHost);

        var now = Now();
        var changed = await PlayingAsync(jam, input.EntryId, input.External is { } external
            ? JsonSerializer.Serialize(new JukeboxJamSong(null, Cut(external.Title, 300) ?? "", (external.Artists ?? []).Take(10).Select(a => Cut(a, 200)!).ToList(),
                Cut(external.Album, 300), Math.Max(0, external.DurationMs ?? 0), null, external.ArtworkUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    && uri.Scheme is "https" or "http" ? Cut(url, 1000) : null), Json)
            : null, input.State, input.PositionMs, cancellationToken);
        jam.HostSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);
        if (changed)
            Publish(jam, JukeboxJamChangeKind.NowPlaying);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>
    /// Records what plays (the host's report, or the player's for the server's audio and renderers); whether it is a
    /// change worth telling.
    /// </summary>
    internal async Task<bool> PlayingAsync(JamSession jam, Guid? entryId, string? external, JukeboxJamPlaybackState state, int positionMs,
        CancellationToken cancellationToken)
    {
        var now = Now();
        var changed = jam.CurrentEntryId != entryId || jam.ExternalNowPlaying != external || jam.Paused != (state != JukeboxJamPlaybackState.Playing);
        if (entryId is { } id && id != jam.CurrentEntryId)
        {
            var entry = await db.PlaylistEntries.SingleOrDefaultAsync(e => e.Id == id && e.PlaylistId == jam.PlaylistId, cancellationToken)
                        ?? throw ArcanistException.Field("entryId", ErrorCodes.NotFound);
            entry.PlayedAt ??= now;
            if (jam.PinnedEntryId == id)
                jam.PinnedEntryId = null;
        }

        jam.CurrentEntryId = state == JukeboxJamPlaybackState.Stopped ? null : entryId;
        jam.ExternalNowPlaying = state == JukeboxJamPlaybackState.Stopped ? null : external;
        jam.Paused = state != JukeboxJamPlaybackState.Playing;
        jam.PositionMs = Math.Max(0, positionMs);
        jam.PositionAt = now;
        if (changed)
            jam.LastActivityAt = now;
        return changed;
    }

    /// <summary>
    /// Says a DJ chooses what plays (Ultimate's AI DJ), or no longer; everyone in the Jam sees it. Host only. It is
    /// taken off by itself when the host's client is gone for 5 minutes.
    /// </summary>
    public async Task<JukeboxJam> SetDjAsync(string code, SetJukeboxJamDjInput input, CancellationToken cancellationToken)
    {
        var (jam, caller) = await OpenAsync(code, null, cancellationToken);
        RequireOpen(jam);
        RequireHost(jam, caller);
        var kind = input.Kind == JukeboxJamDjKind.UltimateAiDj ? JamDj.UltimateAiDj : JamDj.None;
        if (kind == JamDj.None)
        {
            jam.DjKind = JamDj.None;
            jam.DjName = null;
            jam.DjMessage = null;
            jam.DjSince = null;
        }
        else
        {
            jam.DjSince = jam.DjKind == kind ? jam.DjSince ?? Now() : Now();
            jam.DjKind = kind;
            jam.DjName = Cut(input.Name?.Trim(), 100);
            jam.DjMessage = Cut(input.Message?.Trim(), 500);
        }
        jam.HostSeenAt = Now();
        jam.LastActivityAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        Publish(jam, JukeboxJamChangeKind.Dj);
        return await ToModelAsync(jam, caller, cancellationToken);
    }

    /// <summary>The changes of a Jam, for anyone with its code (they say what changed, not what it is).</summary>
    public IObservable<JukeboxJamChange> Watch(string code) => signals.For(code);

    // —— Helpers ——

    private sealed record StoredClient(JukeboxJamClientKind Kind, string? PlayerId, string? Name, string? Version);

    private async Task<(JamSession Jam, Caller Caller)> OpenAsync(string code, string? guestToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 64)
            throw new ArcanistException.NotFound();
        var jam = await db.JamSessions.Include(j => j.Playlist).ThenInclude(p => p.Collaborators)
                      .SingleOrDefaultAsync(j => j.Code == code, cancellationToken)
                  ?? throw new ArcanistException.NotFound();

        if (currentUser.IsAuthenticated)
        {
            var user = guard.Authenticated();
            var participation = user.UserId == jam.HostUserId ? JukeboxJamParticipation.Host
                : PlaylistService.RoleOf(jam.Playlist.OwnerId, jam.Playlist.Collaborators, user.UserId) switch
                {
                    PlaylistRole.Owner or PlaylistRole.Manager => JukeboxJamParticipation.Manager,
                    _ when jam.Playlist.Collaborators.Any(c => c.UserId == user.UserId) => JukeboxJamParticipation.Member,
                    _ => JukeboxJamParticipation.Visitor,
                };
            return (jam, new Caller(user, null, participation));
        }

        // Without an account: only Jams open to guests exist.
        if (!await GuestsOpenAsync(jam, cancellationToken))
            throw new ArcanistException.NotFound();
        if (string.IsNullOrEmpty(guestToken))
            return (jam, new Caller(null, null, JukeboxJamParticipation.Visitor));

        var hash = Hash(guestToken);
        var guest = await db.JamGuests.SingleOrDefaultAsync(g => g.TokenHash == hash && g.JamId == jam.Id, cancellationToken)
                    ?? throw new ArcanistException.Unauthorized();
        if (guest.Banned)
            throw new ArcanistException.Forbidden(ErrorCodes.JamGuestRemoved);
        if (guest.LastSeenAt < Now() - TimeSpan.FromMinutes(1))
        {
            guest.LastSeenAt = Now();
            await db.SaveChangesAsync(cancellationToken);
        }
        return (jam, new Caller(null, guest, JukeboxJamParticipation.Guest));
    }

    private async Task<bool> GuestsOpenAsync(JamSession jam, CancellationToken cancellationToken)
    {
        if (!jam.AllowGuests || !(await sharing.GetAsync(cancellationToken)).JamGuests)
            return false;
        var host = await users.GetUserAsync(jam.HostUserId, cancellationToken);
        return host is { IsDisabled: false } && host.Has(Permissions.ShareLinks);
    }

    private async Task RequireGuestsOpenAsync(JamSession jam, CancellationToken cancellationToken)
    {
        if (!await GuestsOpenAsync(jam, cancellationToken))
            throw new ArcanistException.Forbidden(ErrorCodes.JamGuestsDisabled);
    }

    private async Task RequireGuestsAsync(UserAccess access, CancellationToken cancellationToken)
    {
        if (!(await sharing.GetAsync(cancellationToken)).JamGuests)
            throw new ArcanistException.Conflict(ErrorCodes.JamGuestsDisabled);
        if (!access.Has(Permissions.ShareLinks))
            throw new ArcanistException.Forbidden(ErrorCodes.JamGuestsDisabled);
    }

    private static void RequireOpen(JamSession jam)
    {
        if (jam.EndedAt is not null)
            throw new ArcanistException.Conflict(ErrorCodes.JamEnded);
    }

    private static void RequireParticipant(Caller caller)
    {
        if (caller.User is null && caller.Guest is null)
            throw new ArcanistException.Unauthorized();
    }

    private static UserAccess RequireHost(JamSession jam, Caller caller) =>
        caller.Participation == JukeboxJamParticipation.Host && caller.User is { } user ? user : throw new ArcanistException.Forbidden(ErrorCodes.JamNotHost);

    /// <summary>A user taking part becomes a member of the Jam's playlist (to add songs and find it again).</summary>
    private async Task EnsureMemberAsync(JamSession jam, UserAccess user, CancellationToken cancellationToken)
    {
        if (user.UserId == jam.HostUserId || jam.Playlist.Collaborators.Any(c => c.UserId == user.UserId))
            return;
        db.PlaylistCollaborators.Add(new PlaylistCollaborator { PlaylistId = jam.PlaylistId, UserId = user.UserId, Role = PlaylistMemberRole.Edit, AddedAt = Now() });
        await db.SaveChangesAsync(cancellationToken);
        playlists.Publish(jam.Playlist, [user.UserId]);
    }

    private async Task<UserAccess> HostAsync(JamSession jam, CancellationToken cancellationToken) =>
        await users.GetUserAsync(jam.HostUserId, cancellationToken) is { IsDisabled: false } host ? host : throw new ArcanistException.Conflict(ErrorCodes.JamEnded);

    /// <summary>Tracks that can be added: the host can listen to them, with a copy in the Jam's libraries.</summary>
    private IQueryable<Data.Entities.Track> Allowed(JamSession jam, UserAccess host)
    {
        var tracks = catalog.Visible(host);
        if (LibrariesOf(jam) is not { Count: > 0 } libraries)
            return tracks;
        var files = catalog.VisibleFiles(host).Where(f => libraries.Contains(f.LibrarySource.LibraryId));
        return tracks.Where(t => files.Any(f => f.TrackId == t.Id));
    }

    private static List<Guid>? LibrariesOf(JamSession jam) =>
        jam.GuestLibraryIds is { } json ? JsonSerializer.Deserialize<List<Guid>>(json, Json) : null;

    /// <summary>The libraries chosen (those the user can access), or null for all of them.</summary>
    private static Task<List<Guid>?> LibrariesAsync(UserAccess access, IReadOnlyList<Guid>? ids, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (ids is not { Count: > 0 })
            return Task.FromResult<List<Guid>?>(null);
        var chosen = ids.Distinct().ToList();
        if (chosen.Any(id => id == Libraries.VirtualLibraries.All))
            return Task.FromResult<List<Guid>?>(null);
        if (!chosen.All(access.CanAccessLibrary))
            throw ArcanistException.Field("guestLibraryIds", ErrorCodes.NotFound);
        return Task.FromResult<List<Guid>?>(chosen);
    }

    /// <summary>The waiting entries in the order they will play: pinned, then most voted (voting Jams), then arrival.</summary>
    internal async Task<List<Data.Entities.PlaylistEntry>> WaitingAsync(JamSession jam, CancellationToken cancellationToken)
    {
        var waiting = await db.PlaylistEntries.AsNoTracking().Where(e => e.PlaylistId == jam.PlaylistId && e.PlayedAt == null)
            .OrderBy(e => e.Position).ToListAsync(cancellationToken);
        var votes = jam.Voting ? await VotesAsync(jam, cancellationToken) : [];
        return waiting
            .OrderByDescending(e => e.Id == jam.PinnedEntryId)
            .ThenByDescending(e => votes.GetValueOrDefault(e.Id))
            .ThenBy(e => e.Position)
            .ToList();
    }

    private Task<Dictionary<Guid, int>> VotesAsync(JamSession jam, CancellationToken cancellationToken) =>
        db.JamVotes.Where(v => v.JamId == jam.Id).GroupBy(v => v.EntryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    private async Task<Data.Entities.PlaylistEntry> WaitingEntryAsync(JamSession jam, Guid? entryId, CancellationToken cancellationToken) =>
        entryId is { } id && id != jam.CurrentEntryId
            ? await db.PlaylistEntries.SingleOrDefaultAsync(e => e.Id == id && e.PlaylistId == jam.PlaylistId && e.PlayedAt == null, cancellationToken)
              ?? throw ArcanistException.Field("entryId", ErrorCodes.NotFound)
            : throw ArcanistException.Field("entryId", ErrorCodes.NotFound);

    private async Task RenumberAsync(JamSession jam, CancellationToken cancellationToken)
    {
        var entries = await db.PlaylistEntries.Where(e => e.PlaylistId == jam.PlaylistId).OrderBy(e => e.Position).ToListAsync(cancellationToken);
        for (var i = 0; i < entries.Count; i++)
            entries[i].Position = i;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DateTime?> NextVoteAtAsync(JamSession jam, Caller caller, CancellationToken cancellationToken)
    {
        if (!jam.Voting || (caller.User is null && caller.Guest is null))
            return null;
        var votes = db.JamVotes.Where(v => v.JamId == jam.Id && !v.FromAdding);
        votes = caller.User is { } user ? votes.Where(v => v.UserId == user.UserId) : votes.Where(v => v.GuestId == caller.Guest!.Id);
        var last = await votes.OrderByDescending(v => v.CastAt).Select(v => (DateTime?)v.CastAt).FirstOrDefaultAsync(cancellationToken);
        return last is { } at && at + TimeSpan.FromSeconds(VoteIntervalSeconds) > Now() ? at + TimeSpan.FromSeconds(VoteIntervalSeconds) : null;
    }

    /// <summary>Songs as the host has them, to show to anyone (never their streams: those are the host's).</summary>
    private async Task<Dictionary<Guid, JukeboxJamSong>> SongsAsync(UserAccess host, IReadOnlyCollection<Guid> trackIds, CancellationToken cancellationToken)
    {
        Dictionary<Guid, Catalog.Track> tracks;
        using (HttpCurrentUser.ActAs(host, DateTimeOffset.MinValue))
            tracks = await catalog.VisibleTracksAsync(host, trackIds, cancellationToken);
        return tracks.ToDictionary(t => t.Key, t => new JukeboxJamSong(
            t.Key,
            t.Value.Title,
            t.Value.Artists.Where(a => a.Role != ArtistRole.Featured).Select(a => a.Name).DefaultIfEmpty(t.Value.AlbumArtist.Name).ToList(),
            t.Value.Album.Title,
            t.Value.DurationMs,
            t.Value.ArtworkId,
            null));
    }

    private async Task<JukeboxJam> ToModelAsync(JamSession jam, Caller caller, CancellationToken cancellationToken)
    {
        var host = await users.GetUserAsync(jam.HostUserId, cancellationToken);
        var waiting = jam.EndedAt is null ? await WaitingAsync(jam, cancellationToken) : [];
        var played = await db.PlaylistEntries.AsNoTracking().Where(e => e.PlaylistId == jam.PlaylistId && e.PlayedAt != null)
            .OrderByDescending(e => e.PlayedAt).Take(21).ToListAsync(cancellationToken);
        var current = played.FirstOrDefault(e => e.Id == jam.CurrentEntryId);
        played = played.Where(e => e.Id != jam.CurrentEntryId).Take(20).ToList();
        var shown = waiting.Take(500).Concat(played).Concat(current is null ? [] : [current]).ToList();

        var songs = host is null ? [] : await SongsAsync(host, shown.Select(e => e.TrackId).Distinct().ToList(), cancellationToken);
        var votes = jam.Voting ? await VotesAsync(jam, cancellationToken) : [];
        var names = await playlists.UsersAsync(shown.Where(e => e.AddedByUserId != null).Select(e => e.AddedByUserId!.Value).Append(jam.HostUserId).Distinct().ToList(),
            cancellationToken);
        var guestIds = shown.Where(e => e.AddedByGuestId != null).Select(e => e.AddedByGuestId!.Value).Distinct().ToList();
        var guestNames = await db.JamGuests.AsNoTracking().Where(g => guestIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);

        JukeboxJamEntry? Entry(Data.Entities.PlaylistEntry e, JukeboxJamEntryState state) =>
            songs.TryGetValue(e.TrackId, out var song)
                ? new JukeboxJamEntry(
                    e.Id,
                    song,
                    state,
                    e.AddedByDj ? null : e.AddedByGuestId is { } g ? guestNames.GetValueOrDefault(g) : e.AddedByUserId is { } u ? names.GetValueOrDefault(u)?.Name : null,
                    e.AddedByGuestId is not null,
                    e.AddedByDj,
                    (caller.User is { } me && e.AddedByUserId == me.UserId) || (caller.Guest is { } guest && e.AddedByGuestId == guest.Id),
                    state == JukeboxJamEntryState.Waiting ? votes.GetValueOrDefault(e.Id) : 0,
                    e.Id == jam.PinnedEntryId,
                    e.AddedAt,
                    e.PlayedAt)
                : null;

        JukeboxJamHostClient? client = null;
        if (jam.Target == JamTarget.Client && jam.HostClient is { } stored && JsonSerializer.Deserialize<StoredClient>(stored, Json) is { } info)
            client = new JukeboxJamHostClient(info.Kind, info.PlayerId, info.Name, info.Version,
                jam.HostSeenAt is { } seen && Now() - seen < HostOnline, jam.HostSeenAt);

        var nextVote = await NextVoteAtAsync(jam, caller, cancellationToken);
        int? addsLeft = null;
        if (caller.Guest is { } g2)
        {
            var limit = (await sharing.GetAsync(cancellationToken)).GuestAddLimit;
            var recent = await db.PlaylistEntries.CountAsync(e => e.PlaylistId == jam.PlaylistId && e.AddedByGuestId == g2.Id && e.AddedAt > Now() - GuestWindow, cancellationToken);
            addsLeft = Math.Max(0, limit - recent);
        }

        IReadOnlyList<JukeboxJamGuestInfo>? guests = null;
        if (caller.CanControl)
        {
            var added = await db.PlaylistEntries.Where(e => e.PlaylistId == jam.PlaylistId && e.AddedByGuestId != null)
                .GroupBy(e => e.AddedByGuestId!.Value).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
            guests = (await db.JamGuests.AsNoTracking().Where(x => x.JamId == jam.Id).OrderBy(x => x.JoinedAt).ToListAsync(cancellationToken))
                .Select(x => new JukeboxJamGuestInfo(x.Id, x.Name, x.JoinedAt, added.GetValueOrDefault(x.Id), x.Banned)).ToList();
        }

        var external = jam.ExternalNowPlaying is { } json ? JsonSerializer.Deserialize<JukeboxJamSong>(json, Json) : null;
        return new JukeboxJam(
            jam.Code,
            jam.Playlist.Name,
            jam.PlaylistId,
            names.GetValueOrDefault(jam.HostUserId)?.Name ?? "",
            await links.JamUrlAsync(jam.Code, cancellationToken),
            ToModel(jam.Target),
            jam.RendererId,
            client,
            jam.Voting,
            VoteIntervalSeconds,
            jam.AllowGuests,
            jam.DjKind == JamDj.None ? null : new JukeboxJamDjSession(JukeboxJamDjKind.UltimateAiDj, jam.DjName, jam.DjMessage, jam.DjSince ?? jam.StartedAt),
            new JukeboxJamNowPlaying(current is null ? null : Entry(current, JukeboxJamEntryState.Playing), external, jam.Paused, jam.PositionMs, jam.PositionAt),
            waiting.Take(500).Select(e => Entry(e, JukeboxJamEntryState.Waiting)).OfType<JukeboxJamEntry>().ToList(),
            played.Select(e => Entry(e, JukeboxJamEntryState.Played)).OfType<JukeboxJamEntry>().ToList(),
            new JukeboxJamYou(caller.Participation, caller.Guest?.Name ?? (caller.User is { } u2 ? names.GetValueOrDefault(u2.UserId)?.Name ?? (await playlists.UsersAsync([u2.UserId], cancellationToken)).GetValueOrDefault(u2.UserId)?.Name : null),
                caller.Guest?.Id, caller.CanControl, nextVote, addsLeft),
            guests,
            caller.CanControl ? LibrariesOf(jam) : null,
            jam.StartedAt,
            jam.EndedAt);
    }

    private static JukeboxJamPlaysOn ToModel(JamTarget target) => target switch
    {
        JamTarget.Server => JukeboxJamPlaysOn.Server,
        JamTarget.Dlna => JukeboxJamPlaysOn.Dlna,
        _ => JukeboxJamPlaysOn.Client,
    };

    private void Changed(JamSession jam, JukeboxJamChangeKind kind)
    {
        playlists.Publish(jam.Playlist, []);
        Publish(jam, kind);
    }

    private void Publish(JamSession jam, JukeboxJamChangeKind kind) =>
        signals.Publish(jam.Id, new JukeboxJamChange(jam.Code, kind, null, kind == JukeboxJamChangeKind.Host ? jam.HostDeviceId : null, Now()));

    internal static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string? Cut(string? text, int length) => text is null ? null : text.Length <= length ? text : text[..length];

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
