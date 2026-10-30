namespace Arcanist.Core.Videos;

/// <summary>A line of lyrics on a video's timeline: shown from <paramref name="StartMs"/> until <paramref name="EndMs"/>.</summary>
/// <param name="SongTimeMs">Where the line starts in the song (lines carried over from the song's timing).</param>
public sealed record VideoLyricLine(int StartMs, int EndMs, string Text, int? SongTimeMs);

/// <summary>
/// Puts lyrics on a music video's timeline. A video is not its song: it can open with a scene, stop the song for a
/// dialogue, cut a verse, repeat a chorus or end with an outro. The song's timed lyrics follow the alignment (the
/// stretches where the video plays the song, <see cref="AlignedSegment"/>): each line goes wherever its part of the
/// song plays in the video (more than once if the video repeats it, never if the video cuts it), and nothing shows
/// over the video's own scenes. Lyrics timed on the video's own audio (a live video, another edit) are already on its
/// timeline and only get their ends.
/// </summary>
public static class VideoLyrics
{
    /// <summary>What is left of a line at a cut for it to be worth showing.</summary>
    public const int ShortestPieceMs = 600;

    /// <summary>
    /// The song's lines (<paramref name="songLines"/>, by time in the song, empty texts as breaks) on the video's
    /// timeline through <paramref name="segments"/>. A line ends where the next one starts, or at the end of the song
    /// (<paramref name="songDurationMs"/>), and is cut where the video leaves the song.
    /// </summary>
    public static IReadOnlyList<VideoLyricLine> FromSong(
        IReadOnlyList<(int TimeMs, string Text)> songLines, int songDurationMs, IReadOnlyList<AlignedSegment> segments)
    {
        var lines = songLines.OrderBy(l => l.TimeMs).ToList();
        var placed = new List<Piece>();
        if (lines.Count == 0)
            return [];

        foreach (var segment in segments.Where(s => s.FromTrack && s.VideoEndMs > s.VideoStartMs).OrderBy(s => s.VideoStartMs))
        {
            var rate = segment.Rate > 0 ? segment.Rate : 1.0;
            var songStart = (double)segment.TrackStartMs;
            var songEnd = songStart + (segment.VideoEndMs - segment.VideoStartMs) * rate;
            for (var i = 0; i < lines.Count; i++)
            {
                var lineStart = lines[i].TimeMs;
                var lineEnd = i + 1 < lines.Count ? lines[i + 1].TimeMs : Math.Max(songDurationMs, lineStart);
                if (lineEnd <= songStart || lineStart >= songEnd)
                    continue;
                var from = Math.Max(lineStart, songStart);
                var to = Math.Min(lineEnd, songEnd);
                var start = segment.VideoStartMs + (int)Math.Round((from - songStart) / rate);
                var end = segment.VideoStartMs + (int)Math.Round((to - songStart) / rate);
                placed.Add(new Piece(new VideoLyricLine(start, Math.Min(end, segment.VideoEndMs), lines[i].Text, lineStart), lineEnd - lineStart, from, to));
            }
        }

        // A sliver of a line at a cut (its last syllable, or one that only just started) is not shown.
        return Join(placed).Where(p => p.Line.EndMs - p.Line.StartMs >= Math.Min(ShortestPieceMs, p.LengthMs)).Select(p => p.Line).ToList();
    }

    /// <summary>
    /// Lines timed on the video's own audio: each ends where the next starts, or at the end of the video. They show
    /// over the whole video: the timing already left out what is not sung.
    /// </summary>
    public static IReadOnlyList<VideoLyricLine> FromVideo(IReadOnlyList<(int TimeMs, string Text)> videoLines, int videoDurationMs)
    {
        var lines = videoLines.OrderBy(l => l.TimeMs).Where(l => l.TimeMs < videoDurationMs).ToList();
        var placed = new List<VideoLyricLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var end = i + 1 < lines.Count ? lines[i + 1].TimeMs : videoDurationMs;
            if (end > lines[i].TimeMs)
                placed.Add(new VideoLyricLine(lines[i].TimeMs, end, lines[i].Text, null));
        }

        return placed;
    }

    /// <summary>A line's piece in one stretch: <paramref name="SongFromMs"/> to <paramref name="SongToMs"/> of the song.</summary>
    private sealed record Piece(VideoLyricLine Line, int LengthMs, double SongFromMs, double SongToMs);

    /// <summary>
    /// A line that runs on from one stretch of the song into the next (the alignment split a continuous part of the
    /// song in two) is one line, not the same line twice. A line the video plays again (it starts over) stays two.
    /// </summary>
    private static List<Piece> Join(List<Piece> placed)
    {
        placed.Sort((a, b) => a.Line.StartMs.CompareTo(b.Line.StartMs));
        var joined = new List<Piece>(placed.Count);
        foreach (var piece in placed)
        {
            if (joined.Count > 0 && joined[^1] is var last && last.Line.SongTimeMs == piece.Line.SongTimeMs
                && Math.Abs(piece.Line.StartMs - last.Line.EndMs) <= 100 && Math.Abs(piece.SongFromMs - last.SongToMs) <= 100)
                joined[^1] = last with { Line = last.Line with { EndMs = Math.Max(last.Line.EndMs, piece.Line.EndMs) }, SongToMs = piece.SongToMs };
            else
                joined.Add(piece);
        }

        return joined;
    }
}