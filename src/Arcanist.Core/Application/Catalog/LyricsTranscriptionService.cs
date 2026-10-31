using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// Recognizing the words of songs without lyrics, for players: anyone who can hear a song can ask for it (the Lyrics
/// Sync extension listens to it); what the model writes is a draft nobody sees as the song's lyrics until someone who
/// may edit the song's tags (EDIT_METADATA, and a library with the song they may change) corrects and accepts it.
/// </summary>
public sealed class LyricsTranscriptionService(
    ArcanistDbContext db, AccessGuard guard, CatalogService catalog, LyricsTranscription transcription, ExtensionHost extensions, TimeProvider time)
{
    public const int MaxLines = 2000;
    public const int MaxLineLength = 500;

    /// <summary>Below this confidence a line is flagged for a closer look.</summary>
    public const double UncertainBelow = 0.6;

    public async Task<LyricsTranscriptionStatus> StatusAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        await EnsureVisibleAsync(access, trackId, cancellationToken);
        return await StatusOfAsync(access, trackId, cancellationToken);
    }

    /// <summary>Asks the extension to recognize the song's words (again, after no voice or a failure).</summary>
    public async Task<LyricsTranscriptionStatus> RequestAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        await EnsureVisibleAsync(access, trackId, cancellationToken);
        var status = await StatusOfAsync(access, trackId, cancellationToken);
        switch (status.State)
        {
            case LyricsTranscriptionState.HasLyrics:
                throw new ArcanistException.Conflict(ErrorCodes.HasLyrics);
            case LyricsTranscriptionState.Unavailable:
                throw new ArcanistException.Conflict(status.Reason ?? ErrorCodes.LyricsSyncUnavailable);
            case LyricsTranscriptionState.NotRequested or LyricsTranscriptionState.NoVoice or LyricsTranscriptionState.Failed:
                transcription.Request(trackId);
                return await StatusOfAsync(access, trackId, cancellationToken);
            default:
                // Already queued, being recognized or waiting for review.
                return status;
        }
    }

    /// <summary>Saves the reviewer's changes to the draft (it stays a draft).</summary>
    public async Task<LyricsTranscriptionStatus> SaveDraftAsync(Guid trackId, IReadOnlyList<LyricsLineInput> lines, CancellationToken cancellationToken)
    {
        var access = await ReviewerAsync(trackId, cancellationToken);
        var draft = await db.LyricsDrafts.SingleOrDefaultAsync(d => d.TrackId == trackId, cancellationToken) ?? throw new ArcanistException.NotFound();
        draft.Lines = LyricsTranscription.SerializeLines(Merge(LyricsTranscription.ParseLines(draft.Lines), Validated(lines)));
        draft.UpdatedAt = time.GetUtcNow().UtcDateTime;
        draft.EditedByUserId = access.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await StatusOfAsync(access, trackId, cancellationToken);
    }

    /// <summary>
    /// Accepts the draft (with the lines given, or as it is) as the song's lyrics, timed line by line
    /// (<see cref="LyricsSource.Transcribed"/>: lyrics a file brings later replace them).
    /// </summary>
    public async Task<TrackLyrics> AcceptAsync(Guid trackId, IReadOnlyList<LyricsLineInput>? lines, CancellationToken cancellationToken)
    {
        await ReviewerAsync(trackId, cancellationToken);
        var draft = await db.LyricsDrafts.SingleOrDefaultAsync(d => d.TrackId == trackId, cancellationToken) ?? throw new ArcanistException.NotFound();
        var accepted = (lines is null
                ? LyricsTranscription.ParseLines(draft.Lines).Select(l => (l.TimeMs, l.Text)).ToList()
                : Validated(lines).Select(l => (l.TimeMs, l.Text)).ToList())
            .OrderBy(l => l.TimeMs).ToList();
        if (!accepted.Any(l => l.Text.Length > 0))
            throw ArcanistException.Field("lines", ErrorCodes.Required);

        var lyrics = await db.Lyrics.SingleOrDefaultAsync(l => l.TrackId == trackId, cancellationToken);
        if (lyrics is { } existing && (existing.Instrumental || existing.Plain is not null || existing.Synced is not null))
            throw new ArcanistException.Conflict(ErrorCodes.HasLyrics);
        if (lyrics is null)
        {
            lyrics = new Lyrics { TrackId = trackId };
            db.Lyrics.Add(lyrics);
        }

        lyrics.Plain = string.Join('\n', accepted.Select(l => l.Text));
        lyrics.Synced = Lrc.Format(accepted);
        lyrics.Source = LyricsSource.Transcribed;
        lyrics.SyncedSource = null;
        lyrics.ReplacedTimingHash = null;
        lyrics.Instrumental = false;
        lyrics.UpdatedAt = time.GetUtcNow().UtcDateTime;
        db.LyricsDrafts.Remove(draft);
        await db.SaveChangesAsync(cancellationToken);
        transcription.Forget(trackId);
        return await catalog.LyricsAsync(trackId, cancellationToken);
    }

    /// <summary>Throws the draft away; with <paramref name="again"/>, the song is recognized again.</summary>
    public async Task<LyricsTranscriptionStatus> DiscardAsync(Guid trackId, bool again, CancellationToken cancellationToken)
    {
        var access = await ReviewerAsync(trackId, cancellationToken);
        await db.LyricsDrafts.Where(d => d.TrackId == trackId).ExecuteDeleteAsync(cancellationToken);
        transcription.Forget(trackId);
        if (again && LyricsTimingService.StateOf(extensions) == LyricsTimingExtension.Running)
            transcription.Request(trackId);
        return await StatusOfAsync(access, trackId, cancellationToken);
    }

    /// <summary>The drafts waiting for review, of songs the user may review, most recently recognized first.</summary>
    public async Task<LyricsDraftPage> DraftsAsync(int? offset, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.EditMetadata);
        // Libraries the user may change (not only listen to), as for editing tags.
        var libraries = (await db.Libraries.AsNoTracking().Select(l => l.Id).ToListAsync(cancellationToken)).Where(access.CanChange).ToList();
        var visible = catalog.VisibleFiles(access);
        var drafts = db.LyricsDrafts.AsNoTracking()
            .Where(d => visible.Any(f => f.TrackId == d.TrackId && libraries.Contains(f.LibrarySource.LibraryId)));
        var total = await drafts.CountAsync(cancellationToken);
        var rows = await drafts.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.TrackId)
            .Skip(Math.Max(0, offset ?? 0)).Take(Math.Clamp(first ?? 50, 1, 200))
            .Select(d => new
            {
                d.TrackId, d.Track.Title, d.Track.DurationMs, Album = d.Track.Album.Title, ArtworkId = d.Track.ArtworkId ?? d.Track.Album.ArtworkId,
                Artist = d.Track.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
                d.Lines, d.Language, d.CreatedAt, d.UpdatedAt, d.EditedByUserId,
            })
            .ToListAsync(cancellationToken);
        var editors = await NamesAsync(rows.Select(r => r.EditedByUserId), cancellationToken);
        return new LyricsDraftPage(rows.Select(r =>
        {
            var lines = LyricsTranscription.ParseLines(r.Lines);
            return new LyricsDraftSummary(r.TrackId, r.Title, r.Artist, r.Album, r.ArtworkId, r.DurationMs, lines.Count(l => l.Text.Length > 0),
                lines.Count(l => l.Confidence < UncertainBelow), r.Language, r.CreatedAt, r.UpdatedAt,
                r.EditedByUserId is { } editor ? editors.GetValueOrDefault(editor) : null);
        }).ToList(), total);
    }

    private async Task<LyricsTranscriptionStatus> StatusOfAsync(UserAccess access, Guid trackId, CancellationToken cancellationToken)
    {
        var running = LyricsTimingService.StateOf(extensions) == LyricsTimingExtension.Running;
        var reviewBlocker = await ReviewBlockerAsync(access, trackId, cancellationToken);
        var canReview = reviewBlocker is null;

        if (await LyricsTranscription.HasWordsAsync(db, trackId, cancellationToken))
            return new LyricsTranscriptionStatus(trackId, LyricsTranscriptionState.HasLyrics, null, false, false, null);

        var draft = await db.LyricsDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.TrackId == trackId, cancellationToken);
        if (draft is not null)
        {
            var editor = draft.EditedByUserId is { } id ? (await NamesAsync([id], cancellationToken)).GetValueOrDefault(id) : null;
            var info = canReview
                ? new LyricsDraftInfo(LyricsTranscription.ParseLines(draft.Lines).Select(l => new LyricsDraftLine(l.TimeMs, l.Text, l.Confidence)).ToList(),
                    draft.Language, draft.Model, draft.CreatedAt, draft.UpdatedAt, editor)
                : null;
            return new LyricsTranscriptionStatus(trackId, LyricsTranscriptionState.NeedsReview, canReview ? null : reviewBlocker, false, canReview, info);
        }

        if (!running)
            return new LyricsTranscriptionStatus(trackId, LyricsTranscriptionState.Unavailable, ErrorCodes.LyricsSyncUnavailable, false, false, null);
        if (!await db.MediaFiles.AnyAsync(f => f.TrackId == trackId && f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local,
                cancellationToken))
            return new LyricsTranscriptionStatus(trackId, LyricsTranscriptionState.Unavailable, ErrorCodes.NoLocalCopy, false, false, null);

        var state = transcription.ProgressOf(trackId) switch
        {
            TranscriptionProgress.Queued => LyricsTranscriptionState.Queued,
            TranscriptionProgress.Working => LyricsTranscriptionState.Working,
            TranscriptionProgress.NoVoice => LyricsTranscriptionState.NoVoice,
            TranscriptionProgress.Failed => LyricsTranscriptionState.Failed,
            _ => LyricsTranscriptionState.NotRequested,
        };
        var canRequest = state is LyricsTranscriptionState.NotRequested or LyricsTranscriptionState.NoVoice or LyricsTranscriptionState.Failed;
        return new LyricsTranscriptionStatus(trackId, state, null, canRequest, canReview, null);
    }

    /// <summary>Why the user cannot review the song's draft; null when they can (as for editing its tags).</summary>
    private async Task<string?> ReviewBlockerAsync(UserAccess access, Guid trackId, CancellationToken cancellationToken)
    {
        if (!access.Has(Permissions.EditMetadata))
            return ErrorCodes.Forbidden;
        var libraries = await catalog.VisibleFiles(access).Where(f => f.TrackId == trackId).Select(f => f.LibrarySource.LibraryId).Distinct()
            .ToListAsync(cancellationToken);
        return libraries.Any(access.CanChange) ? null : ErrorCodes.LibraryReadOnly;
    }

    private async Task<UserAccess> ReviewerAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.EditMetadata);
        await EnsureVisibleAsync(access, trackId, cancellationToken);
        if (await ReviewBlockerAsync(access, trackId, cancellationToken) is { } code)
            throw new ArcanistException.Forbidden(code);
        return access;
    }

    private async Task EnsureVisibleAsync(UserAccess access, Guid trackId, CancellationToken cancellationToken)
    {
        if (!await catalog.VisibleFiles(access).AnyAsync(f => f.TrackId == trackId, cancellationToken))
            throw new ArcanistException.NotFound();
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        if (wanted.Count == 0)
            return [];
        return await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.UserName, cancellationToken);
    }

    /// <summary>The lines as given, checked: at most <see cref="MaxLines"/>, times not negative, texts not too long.</summary>
    private static List<LyricsLineInput> Validated(IReadOnlyList<LyricsLineInput> lines)
    {
        var errors = new ValidationErrors();
        if (lines.Count > MaxLines)
            errors.Add("lines", ErrorCodes.TooMany);
        for (var i = 0; i < lines.Count && i < MaxLines; i++)
        {
            if (lines[i].TimeMs < 0)
                errors.Add($"lines[{i}].timeMs", ErrorCodes.InvalidFormat);
            if ((lines[i].Text ?? "").Length > MaxLineLength)
                errors.Add($"lines[{i}].text", ErrorCodes.InvalidFormat);
        }

        errors.ThrowIfAny();
        return lines.Select(l => new LyricsLineInput(l.TimeMs, (l.Text ?? "").Trim())).OrderBy(l => l.TimeMs).ToList();
    }

    /// <summary>
    /// The edited lines, keeping the model's confidence on those left as it wrote them (same words, about the same
    /// time): what a person changed is no longer the model's guess.
    /// </summary>
    private static List<TranscribedLine> Merge(IReadOnlyList<TranscribedLine> before, List<LyricsLineInput> after) =>
        after.Select(line => new TranscribedLine(line.TimeMs, line.Text,
            before.FirstOrDefault(b => b.Text == line.Text && Math.Abs(b.TimeMs - line.TimeMs) <= 2000)?.Confidence)).ToList();
}
