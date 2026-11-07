using System.Text.Json;
using System.Text.RegularExpressions;
using Arcanist.Core.Application.Backups;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Extensibility;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A part of a line with its reading above it (furigana); a part without one is written as it is.</summary>
public sealed record LyricsRuby(string Text, string? Reading);

/// <summary>How a line of lyrics is pronounced.</summary>
/// <param name="Romanized">The line in the Latin alphabet (romaji, pinyin, Revised Romanization…).</param>
/// <param name="Ruby">The line in parts with their readings (furigana, for Japanese); null for other scripts.</param>
public sealed record LyricsLineReading(string? Romanized, IReadOnlyList<LyricsRuby>? Ruby);

public enum LyricsTranslationStatus
{
    /// <summary>Translated: <c>lines</c> has them.</summary>
    Ready,

    /// <summary>It can be translated, and nobody asked for it yet: <c>requestLyricsTranslation</c>.</summary>
    NotRequested,

    /// <summary>Waiting its turn (<c>position</c>): ask again, or follow <c>lyricsTranslationReady</c>.</summary>
    Queued,

    /// <summary>Being translated now.</summary>
    Working,

    /// <summary>It could not be translated (it is tried again an hour later).</summary>
    Failed,

    /// <summary>Nothing translates into that language here (no extension, or not that language).</summary>
    Unavailable,

    /// <summary>The lyrics are in that language already.</summary>
    SameLanguage,
}

/// <summary>A song's lyrics in another language.</summary>
/// <param name="Lines">The translated lines, one per line of the lyrics as <c>trackLyrics</c> gives them (the synchronized
/// ones, or the plain lyrics by line), so their times apply.</param>
/// <param name="From">The language of the lyrics, when known.</param>
/// <param name="Engine">What translated them ("Ollama · llama3.1", "M2M100"), to say so.</param>
/// <param name="Position">While <c>QUEUED</c>, how many are before it (0: next).</param>
public sealed record LyricsTranslationResult(
    Guid TrackId,
    string Language,
    LyricsTranslationStatus Status,
    string? From,
    IReadOnlyList<string>? Lines,
    string? Engine,
    int? Position);

/// <summary>A translation asked for is ready (or failed): read <c>lyricsTranslation</c> again.</summary>
public sealed record LyricsTranslationReady(Guid TrackId, string Language, LyricsTranslationStatus Status);

/// <summary>
/// Pronunciation and translation of lyrics, from the extension that provides them (<see cref="ILyricsReadings"/>,
/// <see cref="ILyricsTranslator"/>): readings are worked out when lyrics are read and kept by their text; translations
/// are asked for, made in the background one at a time, and kept for everyone by text and language.
/// </summary>
public sealed partial class LyricsLanguages(
    ExtensionHost extensions,
    IServiceProvider services,
    IArcanistDbContextFactory databases,
    TimeProvider time,
    ILogger<LyricsLanguages> logger)
{
    /// <summary>A translation that failed is not tried again before this.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private readonly List<Job> _queue = [];
    private readonly Dictionary<string, DateTime> _failed = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wake = new(0);
    private readonly LiveFeed<LyricsTranslationReady> _ready = new();
    private Job? _working;

    public ILyricsReadings? Reader => extensions.Active<ILyricsReadings>(services);

    public ILyricsTranslator? Translator => extensions.Active<ILyricsTranslator>(services);

    /// <summary>The languages lyrics can be translated into here (none without a translator).</summary>
    public IReadOnlyList<string> TranslationLanguages => Translator?.Languages ?? [];

    /// <summary>
    /// The lines players show: the synchronized ones, else the plain lyrics by line (blank lines kept, so each reading
    /// or translation goes with its line).
    /// </summary>
    public static IReadOnlyList<string> LinesOf(string? plain, IReadOnlyList<LyricsLine>? synced) =>
        synced is { Count: > 0 } ? synced.Select(l => l.Text).ToList()
        : plain is { Length: > 0 } ? plain.ReplaceLineEndings("\n").Split('\n')
        : [];

    public static string HashOf(IReadOnlyList<string> lines) => LyricsTiming.HashOf(string.Join('\n', lines));

    // —— Readings ——

    /// <summary>
    /// The language of the lines and a reading per line, from what is kept or worked out now; nulls without a
    /// reader, or for lyrics in a script that needs no reading.
    /// </summary>
    public async Task<(string? Language, IReadOnlyList<LyricsLineReading?>? Readings)> ReadingsAsync(IReadOnlyList<string> lines,
        CancellationToken cancellationToken)
    {
        if (Reader is not { } reader || lines.All(string.IsNullOrWhiteSpace))
            return (null, null);

        var hash = HashOf(lines);
        await using var db = databases.CreateDbContext();
        var kept = await db.LyricsReadings.SingleOrDefaultAsync(r => r.TextHash == hash, cancellationToken);
        if (kept is not null && kept.Version == reader.Version)
            return (kept.Language, kept.Readings is null ? null : JsonSerializer.Deserialize<List<LyricsLineReading?>>(kept.Readings, JsonSerializerOptions.Web));

        string? language;
        IReadOnlyList<LyricsLineReading?>? readings = null;
        try
        {
            language = reader.LanguageOf(lines);
            if (language is not null)
            {
                var read = reader.Read(lines, language);
                readings = lines.Select((_, i) => i < read.Count && read[i] is { } r
                        ? new LyricsLineReading(r.Romanized, r.Ruby?.Select(s => new LyricsRuby(s.Text, s.Reading)).ToList())
                        : null)
                    .ToList();
                // Lyrics whose lines need no reading (in the Latin alphabet): only their language is said.
                if (readings.All(r => r is null))
                    readings = null;
            }
        }
        catch (Exception e)
        {
            // A reader that fails never keeps the lyrics from being read.
            logger.LogWarning(e, "Reading lyrics failed");
            return (null, null);
        }

        var json = readings is null ? null : JsonSerializer.Serialize(readings, JsonSerializerOptions.Web);
        if (kept is null)
            db.LyricsReadings.Add(new LyricsReadingCache
            {
                TextHash = hash, Version = reader.Version, Language = language, Readings = json, CreatedAt = time.GetUtcNow().UtcDateTime,
            });
        else
            (kept.Version, kept.Language, kept.Readings, kept.CreatedAt) = (reader.Version, language, json, time.GetUtcNow().UtcDateTime);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two readers of the same lyrics at once: the other one kept them.
        }

        return (language, readings);
    }

    // —— Translations ——

    /// <summary>A language as ISO 639-1 (or 639-3), lower case, with an optional region or script: <c>es</c>, <c>pt-BR</c>, <c>zh-Hant</c>.</summary>
    public static string? Language(string? language) =>
        language?.Trim() is { } code && LanguageCode().IsMatch(code) ? code.Split('-')[0].ToLowerInvariant() + code[code.Split('-')[0].Length..] : null;

    /// <summary>The translation of a song's lines into <paramref name="language"/>, asking for it when <paramref name="request"/>.</summary>
    public async Task<LyricsTranslationResult> TranslationAsync(Guid trackId, IReadOnlyList<string> lines, string? from, string language, bool request,
        CancellationToken cancellationToken)
    {
        LyricsTranslationResult Result(LyricsTranslationStatus status, int? position = null) =>
            new(trackId, language, status, from, null, null, position);

        if (from is not null && Primary(from) == Primary(language))
            return Result(LyricsTranslationStatus.SameLanguage);
        if (lines.All(string.IsNullOrWhiteSpace))
            return Result(LyricsTranslationStatus.Unavailable);

        var hash = HashOf(lines);
        await using (var db = databases.CreateDbContext())
        {
            var kept = await db.LyricsTranslations.AsNoTracking().SingleOrDefaultAsync(t => t.TextHash == hash && t.Language == language, cancellationToken);
            if (kept is not null)
                return new LyricsTranslationResult(trackId, language, LyricsTranslationStatus.Ready, kept.From ?? from,
                    JsonSerializer.Deserialize<List<string>>(kept.Lines, JsonSerializerOptions.Web), kept.Engine, null);
        }

        if (Translator is not { } translator || !translator.Languages.Contains(Primary(language), StringComparer.OrdinalIgnoreCase)
                                              && !translator.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
            return Result(LyricsTranslationStatus.Unavailable);

        var key = Key(hash, language);
        lock (_gate)
        {
            if (_working?.Key == key)
            {
                _working.TrackIds.Add(trackId);
                return Result(LyricsTranslationStatus.Working);
            }

            var index = _queue.FindIndex(j => j.Key == key);
            if (index >= 0)
            {
                _queue[index].TrackIds.Add(trackId);
                return Result(LyricsTranslationStatus.Queued, index);
            }

            if (_failed.TryGetValue(key, out var failedAt) && time.GetUtcNow().UtcDateTime - failedAt < RetryAfter)
                return Result(LyricsTranslationStatus.Failed);
            if (!request)
                return Result(LyricsTranslationStatus.NotRequested);

            _failed.Remove(key);
            _queue.Add(new Job(key, hash, language, lines, from, [trackId]));
        }

        if (_wake.CurrentCount == 0)
            _wake.Release();
        lock (_gate)
            return Result(LyricsTranslationStatus.Queued, _queue.FindIndex(j => j.Key == key));
    }


    internal IObservable<LyricsTranslationReady> WatchReady(Guid trackId) =>
        new FilteredObservable<LyricsTranslationReady>(_ready, r => r.TrackId == trackId);

    internal Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => _wake.WaitAsync(delay, cancellationToken);

    /// <summary>The next translation to make; null when none waits (or nothing translates any more: they are dropped).</summary>
    internal Job? Take()
    {
        lock (_gate)
        {
            if (Translator is null)
            {
                _queue.Clear();
                return null;
            }

            if (_queue.Count == 0)
                return null;
            _working = _queue[0];
            _queue.RemoveAt(0);
            return _working;
        }
    }

    /// <summary>The translation is done: kept for everyone, or remembered as failed for a while.</summary>
    internal async Task FinishAsync(Job job, IReadOnlyList<string>? lines, string? engine, CancellationToken cancellationToken)
    {
        if (lines is not null)
        {
            await using var db = databases.CreateDbContext();
            if (!await db.LyricsTranslations.AnyAsync(t => t.TextHash == job.Hash && t.Language == job.Language, cancellationToken))
            {
                db.LyricsTranslations.Add(new LyricsTranslation
                {
                    TextHash = job.Hash, Language = job.Language, From = job.From, Lines = JsonSerializer.Serialize(lines, JsonSerializerOptions.Web),
                    Engine = engine?.Length > 100 ? engine[..100] : engine, CreatedAt = time.GetUtcNow().UtcDateTime,
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        HashSet<Guid> trackIds;
        lock (_gate)
        {
            if (lines is null)
                _failed[job.Key] = time.GetUtcNow().UtcDateTime;
            _working = null;
            trackIds = [.. job.TrackIds];
        }

        foreach (var trackId in trackIds)
            _ready.Publish(new LyricsTranslationReady(trackId, job.Language, lines is null ? LyricsTranslationStatus.Failed : LyricsTranslationStatus.Ready));
    }

    /// <summary>Forgets the readings and translations of lyrics no track has any more.</summary>
    internal async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        if (!await db.LyricsReadings.AnyAsync(cancellationToken) && !await db.LyricsTranslations.AnyAsync(cancellationToken))
            return 0;

        var current = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var lyrics in db.Lyrics.AsNoTracking().Select(l => new { l.Plain, l.Synced }).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var synced = lyrics.Synced ?? (lyrics.Plain is { } text && Core.Scanning.Lrc.LooksTimed(text) ? text : null);
            var lines = synced is not null
                ? LinesOf(null, Core.Scanning.Lrc.Parse(synced).Select(l => new LyricsLine(l.TimeMs, l.Text)).ToList())
                : LinesOf(lyrics.Plain, null);
            current.Add(HashOf(lines));
        }

        var readings = await db.LyricsReadings.Select(r => new { r.Id, r.TextHash }).ToListAsync(cancellationToken);
        var translations = await db.LyricsTranslations.Select(t => new { t.Id, t.TextHash }).ToListAsync(cancellationToken);
        var staleReadings = readings.Where(r => !current.Contains(r.TextHash)).Select(r => r.Id).ToList();
        var staleTranslations = translations.Where(t => !current.Contains(t.TextHash)).Select(t => t.Id).ToList();
        foreach (var chunk in staleReadings.Chunk(500))
            await db.LyricsReadings.Where(r => chunk.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        foreach (var chunk in staleTranslations.Chunk(500))
            await db.LyricsTranslations.Where(t => chunk.Contains(t.Id)).ExecuteDeleteAsync(cancellationToken);
        return staleReadings.Count + staleTranslations.Count;
    }

    private static string Primary(string language) => language.Split('-')[0].ToLowerInvariant();

    private static string Key(string hash, string language) => hash + "|" + language;

    internal sealed record Job(string Key, string Hash, string Language, IReadOnlyList<string> Lines, string? From, HashSet<Guid> TrackIds);

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,4})?$")]
    private static partial Regex LanguageCode();
}
