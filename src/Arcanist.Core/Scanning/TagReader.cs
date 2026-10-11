using System.Globalization;
using System.Text.RegularExpressions;
using ATL;

namespace Arcanist.Core.Scanning;

/// <summary>Everything the catalog needs from one audio file's tags and stream properties.</summary>
public sealed record AudioMetadata
{
    public string? Title { get; init; }
    public IReadOnlyList<string> Artists { get; init; } = [];
    public IReadOnlyList<string> FeaturedArtists { get; init; } = [];
    public string? AlbumArtist { get; init; }
    public string? Album { get; init; }
    public IReadOnlyList<string> Composers { get; init; } = [];
    public IReadOnlyList<string> Lyricists { get; init; } = [];
    public string? Conductor { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public int? Year { get; init; }
    public int? TrackNumber { get; init; }
    public int? DiscNumber { get; init; }
    public bool IsCompilation { get; init; }
    public string? SortArtist { get; init; }
    public string? SortAlbumArtist { get; init; }
    public Guid? RecordingMbid { get; init; }
    public Guid? ReleaseMbid { get; init; }
    public Guid? ArtistMbid { get; init; }
    public Guid? AlbumArtistMbid { get; init; }
    public double? ReplayGainTrackGain { get; init; }
    public double? ReplayGainTrackPeak { get; init; }
    public double? ReplayGainAlbumGain { get; init; }
    public double? ReplayGainAlbumPeak { get; init; }
    public string? Format { get; init; }
    public int? BitRate { get; init; }
    public int? SampleRate { get; init; }
    public int? BitDepth { get; init; }
    public int? Channels { get; init; }
    public int DurationMs { get; init; }
    public long AudioDataOffset { get; init; }
    public long AudioDataSize { get; init; }

    /// <summary>Front cover (or the first picture) embedded in the file.</summary>
    public byte[]? EmbeddedPicture { get; init; }
}

/// <summary>Reads tags with ATL.NET. Tolerates the many ways taggers spell the same field.</summary>
public static partial class TagReader
{
    private const int MaxPictureBytes = 20 * 1024 * 1024;

    static TagReader()
    {
        // ATL settings are process-wide. Additional frames (TXXX, Vorbis comments…) carry MusicBrainz IDs and ReplayGain.
        Settings.ReadAllMetaFrames = true;
        Settings.UseFileNameWhenNoTitle = false;
        Settings.NullAbsentValues = true;
    }

    public static AudioMetadata Read(Stream stream, string mimeType)
    {
        var track = new Track(stream, mimeType);
        var fields = NormalizeKeys(track.AdditionalFields);

        var (artists, featured) = SplitFeatured(Split(track.Artist));

        return new AudioMetadata
        {
            Title = Clean(track.Title),
            Artists = artists,
            FeaturedArtists = featured,
            AlbumArtist = Split(track.AlbumArtist).FirstOrDefault(),
            Album = Clean(track.Album),
            Composers = Split(track.Composer),
            Lyricists = Split(track.Lyricist),
            Conductor = Clean(track.Conductor),
            Genres = Split(track.Genre),
            Year = Positive(track.Year) ?? (track.Date is { Year: > 1 } date ? date.Year : null),
            TrackNumber = Positive(track.TrackNumber),
            DiscNumber = Positive(track.DiscNumber),
            IsCompilation = Flag(fields, "tcmp", "compilation", "cpil"),
            SortArtist = Clean(track.SortArtist),
            SortAlbumArtist = Clean(track.SortAlbumArtist),
            // Vorbis/APE/MP4 "MusicBrainz Track Id" holds the recording; ID3 keeps it in a UFID frame.
            RecordingMbid = Mbid(fields, "musicbrainztrackid") ?? fields.Where(f => f.Key.StartsWith("ufid") && f.Key.Contains("musicbrainz"))
                .Select(f => ParseGuid(f.Value)).FirstOrDefault(g => g is not null),
            ReleaseMbid = Mbid(fields, "musicbrainzalbumid"),
            ArtistMbid = Mbid(fields, "musicbrainzartistid"),
            AlbumArtistMbid = Mbid(fields, "musicbrainzalbumartistid"),
            ReplayGainTrackGain = Number(fields, "replaygaintrackgain"),
            ReplayGainTrackPeak = Number(fields, "replaygaintrackpeak"),
            ReplayGainAlbumGain = Number(fields, "replaygainalbumgain"),
            ReplayGainAlbumPeak = Number(fields, "replaygainalbumpeak"),
            Format = Clean(track.AudioFormat?.ShortName),
            BitRate = Positive(track.Bitrate),
            SampleRate = Positive((int)track.SampleRate),
            BitDepth = Positive(track.BitDepth),
            Channels = Positive(track.ChannelsArrangement?.NbChannels ?? 0),
            DurationMs = (int)Math.Min(int.MaxValue, Math.Max(0, track.DurationMs)),
            AudioDataOffset = track.TechnicalInformation?.AudioDataOffset ?? 0,
            AudioDataSize = track.TechnicalInformation?.AudioDataSize ?? 0,
            EmbeddedPicture = Picture(track),
        };
    }

    private static byte[]? Picture(Track track)
    {
        var pictures = track.EmbeddedPictures;
        if (pictures is null || pictures.Count == 0)
            return null;

        var picture = pictures.FirstOrDefault(p => p.PicType == PictureInfo.PIC_TYPE.Front) ?? pictures[0];
        return picture.PictureData is { Length: > 0 and <= MaxPictureBytes } data ? data : null;
    }

    /// <summary>Splits multi-valued text on ATL's display separator and ';'. Never on '/', so "AC/DC" survives.</summary>
    internal static IReadOnlyList<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split([Settings.DisplayValueSeparator, ';', '\0'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Separates "A feat. B" into the main artist A and the featured artist B.</summary>
    internal static (IReadOnlyList<string> Main, IReadOnlyList<string> Featured) SplitFeatured(IReadOnlyList<string> artists)
    {
        var main = new List<string>();
        var featured = new List<string>();

        foreach (var artist in artists)
        {
            var parts = FeaturingPattern().Split(artist, 2);
            main.Add(parts[0].Trim());
            if (parts.Length > 1)
                featured.AddRange(parts[1].Split([',', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return (main.Where(a => a.Length > 0).ToList(), featured);
    }

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturingPattern();

    /// <summary>Maps keys like "TXXX:MusicBrainz Album Id", "MUSICBRAINZ_ALBUMID" or "----:com.apple.iTunes:MusicBrainz Album Id"
    /// to "musicbrainzalbumid".</summary>
    private static Dictionary<string, string> NormalizeKeys(IDictionary<string, string>? fields)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fields is null)
            return result;

        foreach (var (key, value) in fields)
        {
            var name = key;
            foreach (var prefix in new[] { "TXXX:", "----:com.apple.iTunes:", "WM/" })
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    name = name[prefix.Length..];
            }

            var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            result.TryAdd(normalized, value);
        }

        return result;
    }

    private static Guid? Mbid(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? ParseGuid(value) : null;

    private static Guid? ParseGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Multi-valued fields: the first ID.
        var first = value.Split([Settings.DisplayValueSeparator, ';', '/', '\0'], StringSplitOptions.TrimEntries)[0];
        return Guid.TryParse(first, out var guid) ? guid : null;
    }

    private static double? Number(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value))
            return null;

        var match = NumberPattern().Match(value);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();

    private static bool Flag(Dictionary<string, string> fields, params string[] keys) =>
        keys.Any(k => fields.TryGetValue(k, out var v) && (v.Trim() == "1" || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? Positive(int? value) => value is > 0 ? value : null;
}
