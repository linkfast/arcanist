using Arcanist.Core.Videos;

namespace Arcanist.Core.Tests.Videos;

public class VideoLyricsTests
{
    private static readonly (int, string)[] Song =
    [
        (0, ""),
        (10_000, "First verse"),
        (20_000, "Still the verse"),
        (30_000, "Chorus"),
        (40_000, ""),
        (50_000, "Last line"),
    ];

    private const int SongLength = 60_000;

    [Fact]
    public void An_intro_scene_moves_the_song_later_and_shows_nothing_over_it()
    {
        // 15 s of dialogue, then the whole song.
        var lines = VideoLyrics.FromSong(Song, SongLength,
        [
            new AlignedSegment(0, 15_000, false, 0, 1, null),
            new AlignedSegment(15_000, 75_000, true, 0, 1, 0.9),
        ]);

        Assert.DoesNotContain(lines, l => l.StartMs < 15_000);
        Assert.Equal(25_000, lines.Single(l => l.Text == "First verse").StartMs);
        Assert.Equal(35_000, lines.Single(l => l.Text == "First verse").EndMs);
        Assert.Equal(75_000, lines.Single(l => l.Text == "Last line").EndMs);
    }

    [Fact]
    public void A_scene_in_the_middle_stops_the_lyrics_and_they_pick_up_where_the_song_does()
    {
        // The song until 25 s, an 8 s scene, then the song again from 25 s.
        var lines = VideoLyrics.FromSong(Song, SongLength,
        [
            new AlignedSegment(0, 25_000, true, 0, 1, 0.9),
            new AlignedSegment(25_000, 33_000, false, 0, 1, null),
            new AlignedSegment(33_000, 68_000, true, 25_000, 1, 0.9),
        ]);

        var verse = lines.Where(l => l.Text == "Still the verse").ToList();
        Assert.Equal([(20_000, 25_000), (33_000, 38_000)], verse.Select(l => (l.StartMs, l.EndMs)));
        Assert.DoesNotContain(lines, l => l.StartMs >= 25_000 && l.StartMs < 33_000);
        Assert.Equal(38_000, lines.Single(l => l.Text == "Chorus").StartMs);
    }

    [Fact]
    public void A_cut_verse_is_left_out_and_a_repeated_chorus_shows_twice()
    {
        // The intro, straight to the chorus (the verse cut), then the chorus again.
        var lines = VideoLyrics.FromSong(Song, SongLength,
        [
            new AlignedSegment(0, 10_000, true, 0, 1, 0.9),
            new AlignedSegment(10_000, 20_000, true, 30_000, 1, 0.9),
            new AlignedSegment(20_000, 30_000, true, 30_000, 1, 0.9),
        ]);

        Assert.DoesNotContain(lines, l => l.Text.Contains("verse"));
        Assert.Equal([10_000, 20_000], lines.Where(l => l.Text == "Chorus").Select(l => l.StartMs));
    }

    [Fact]
    public void A_line_split_by_the_alignment_is_one_line_and_slivers_at_cuts_are_dropped()
    {
        var lines = VideoLyrics.FromSong(Song, SongLength,
        [
            // The song with a seam in the middle of "Chorus".
            new AlignedSegment(0, 35_000, true, 0, 1, 0.9),
            new AlignedSegment(35_000, 39_800, true, 35_000, 1, 0.9),
            // Then a scene, then back 200 ms before "Last line" ends... of the song: only a sliver of the break.
            new AlignedSegment(39_800, 50_000, false, 0, 1, null),
            new AlignedSegment(50_000, 60_000, true, 50_000, 1, 0.9),
        ]);

        Assert.Equal((30_000, 39_800), lines.Where(l => l.Text == "Chorus").Select(l => (l.StartMs, l.EndMs)).Single());
        Assert.DoesNotContain(lines, l => l.SongTimeMs == 40_000);
    }

    [Fact]
    public void A_faster_video_squeezes_the_lyrics()
    {
        // The song at 1.001× (a 29.97 fps video of a 30 fps master), from the start.
        var lines = VideoLyrics.FromSong(Song, SongLength, [new AlignedSegment(0, 59_940, true, 0, 1.001, 0.9)]);

        Assert.Equal(49_950, lines.Single(l => l.Text == "Last line").StartMs);
    }

    [Fact]
    public void Lyrics_timed_on_the_video_end_where_the_next_line_starts()
    {
        var lines = VideoLyrics.FromVideo([(12_000, "Live verse"), (5_000, ""), (20_000, "Live chorus"), (90_000, "Past the end")], 30_000);

        Assert.Equal([(5_000, 12_000), (12_000, 20_000), (20_000, 30_000)], lines.Select(l => (l.StartMs, l.EndMs)));
    }
}
