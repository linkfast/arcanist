using Arcanist.Core.Media;
using Arcanist.Core.Scanning;

namespace Arcanist.Core.Dlna;

/// <summary>How a copy reaches a DLNA device: as it is, or converted.</summary>
internal enum DlnaFormat
{
    Original,
    Flac,
    /// <summary>Uncompressed 16-bit PCM, DLNA's mandatory lossless format.</summary>
    Lpcm,
    Mp3,
}

/// <summary>
/// The formats offered to DLNA devices, lossless first: the original, then FLAC and LPCM for lossless files, then
/// MP3. Conversions need ffmpeg. Devices that browse pick the first they play; for "play to", Arcanist picks using
/// what the renderer says it plays.
/// </summary>
internal static class DlnaFormats
{
    /// <summary>DLNA flags: streaming transfer, background transfer, connection stalling and DLNA 1.5.</summary>
    private const string Flags = "01700000000000000000000000000000";

    public static string Name(DlnaFormat format) => format.ToString().ToLowerInvariant();

    public static DlnaFormat? Parse(string? name) =>
        Enum.TryParse<DlnaFormat>(name, ignoreCase: true, out var format) && Enum.IsDefined(format) ? format : null;

    public static TranscodeProfile? Profile(DlnaFormat format) => format switch
    {
        DlnaFormat.Flac => TranscodeProfile.For(TranscodeFormat.Flac),
        DlnaFormat.Lpcm => TranscodeProfile.Lpcm,
        DlnaFormat.Mp3 => TranscodeProfile.For(TranscodeFormat.Mp3),
        _ => null,
    };

    /// <summary>The conversions the available ffmpeg can make.</summary>
    public static IReadOnlySet<DlnaFormat> Convertible(FfmpegInstallation? ffmpeg) =>
        ffmpeg is null
            ? new HashSet<DlnaFormat>()
            : new[] { DlnaFormat.Flac, DlnaFormat.Lpcm, DlnaFormat.Mp3 }.Where(f => ffmpeg.Encoders.Contains(Profile(f)!.Encoder)).ToHashSet();

    public static string Extension(DlnaFormat format, string originalPath) => format switch
    {
        DlnaFormat.Original => Path.GetExtension(originalPath).ToLowerInvariant(),
        DlnaFormat.Flac => ".flac",
        DlnaFormat.Lpcm => ".pcm",
        _ => ".mp3",
    };

    public static string ContentType(DlnaFormat format, string originalPath) =>
        format == DlnaFormat.Original ? AudioFormats.MimeType(originalPath) : Profile(format)!.ContentType;

    /// <summary>The formats of a copy, best first.</summary>
    /// <param name="segment">A track of a cue sheet: part of a file, so it is always converted.</param>
    public static IReadOnlyList<DlnaFormat> Offered(string path, bool segment, IReadOnlySet<DlnaFormat> convertible)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var formats = new List<DlnaFormat>();
        if (!segment)
            formats.Add(DlnaFormat.Original);

        if (AudioFormats.IsLossless(path))
        {
            if (convertible.Contains(DlnaFormat.Flac) && (segment || extension != ".flac"))
                formats.Add(DlnaFormat.Flac);
            if (convertible.Contains(DlnaFormat.Lpcm))
                formats.Add(DlnaFormat.Lpcm);
            if (convertible.Contains(DlnaFormat.Mp3))
                formats.Add(DlnaFormat.Mp3);
        }
        else
        {
            if (convertible.Contains(DlnaFormat.Mp3) && (segment || extension != ".mp3"))
                formats.Add(DlnaFormat.Mp3);
            if (convertible.Contains(DlnaFormat.Lpcm))
                formats.Add(DlnaFormat.Lpcm);
        }

        return formats;
    }

    /// <summary>
    /// The best format a renderer plays, from what it says it accepts; the best one offered when it says nothing.
    /// Null when it plays none of them.
    /// </summary>
    public static DlnaFormat? Choose(string path, bool segment, IReadOnlySet<DlnaFormat> convertible, IReadOnlySet<string> accepts)
    {
        var offered = Offered(path, segment, convertible);
        if (accepts.Count == 0 || accepts.Contains("*"))
            return offered.Count > 0 ? offered[0] : null;
        foreach (var format in offered)
        {
            if (accepts.Contains(Normalize(ContentType(format, path))))
                return format;
        }

        return null;
    }

    /// <summary>A MIME type without parameters and with its usual aliases folded.</summary>
    public static string Normalize(string contentType)
    {
        var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "audio/x-flac" => "audio/flac",
            "audio/x-wav" or "audio/wave" or "audio/vnd.wave" => "audio/wav",
            "audio/mp3" or "audio/x-mp3" or "audio/mpeg3" => "audio/mpeg",
            "audio/x-m4a" or "audio/m4a" or "audio/x-mp4" => "audio/mp4",
            "audio/x-ogg" or "application/ogg" => "audio/ogg",
            "audio/x-aac" or "audio/aacp" => "audio/aac",
            _ => type,
        };
    }

    /// <summary>The fourth field of a protocolInfo, also sent as <c>contentFeatures.dlna.org</c>.</summary>
    public static string Features(DlnaFormat format, string originalPath)
    {
        var profile = format switch
        {
            DlnaFormat.Lpcm => "DLNA.ORG_PN=LPCM;",
            DlnaFormat.Mp3 => "DLNA.ORG_PN=MP3;",
            DlnaFormat.Original when Path.GetExtension(originalPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase) => "DLNA.ORG_PN=MP3;",
            _ => "",
        };
        // Originals are seeked by bytes; conversions by time (TimeSeekRange.dlna.org).
        var operations = format == DlnaFormat.Original ? "01" : "10";
        var converted = format == DlnaFormat.Original ? "0" : "1";
        return $"{profile}DLNA.ORG_OP={operations};DLNA.ORG_CI={converted};DLNA.ORG_FLAGS={Flags}";
    }

    public static string ProtocolInfo(DlnaFormat format, string originalPath) =>
        $"http-get:*:{ContentType(format, originalPath)}:{Features(format, originalPath)}";

    /// <summary>What the server can send, for ConnectionManager's source protocols.</summary>
    public static string SourceProtocols() => string.Join(',',
        new[] { "audio/mpeg", "audio/flac", "audio/L16;rate=44100;channels=2", "audio/wav", "audio/mp4", "audio/ogg", "audio/opus", "audio/aac", "audio/x-aiff", "video/mp4" }
            .Select(type => $"http-get:*:{type}:*"));

    /// <summary>A music video as MP4: byte ranges once it is made (the first time it streams as it is made).</summary>
    public const string VideoProtocolInfo = "http-get:*:video/mp4:DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000";
}
