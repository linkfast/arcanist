using System.Text;
using Arcanist.Core.Scanning;

namespace Arcanist.Core.Tests.Scanning;

public sealed class CueSheetTests
{
    private const string Album = """
        REM GENRE "Electronic"
        REM DATE 1997
        REM DISCNUMBER 2
        PERFORMER "Röyksopp"
        TITLE "Melody A.M."
        FILE "Melody A.M..wav" WAVE
          TRACK 01 AUDIO
            TITLE "So Easy"
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            TITLE "Eple"
            PERFORMER "Röyksopp feat. Someone"
            INDEX 00 04:10:50
            INDEX 01 04:12:00
          TRACK 03 AUDIO
            TITLE "Sparks"
            INDEX 01 07:50:37
        """;

    [Fact]
    public void Reads_album_tags_tracks_and_start_times()
    {
        var cue = CueSheet.Parse(Album)!;

        Assert.Equal(("Melody A.M.", "Röyksopp", 1997, "Electronic", 2), (cue.Title, cue.Performer, cue.Year, cue.Genre, cue.DiscNumber));
        var file = Assert.Single(cue.Files);
        Assert.Equal("Melody A.M..wav", file.Name);
        Assert.Equal(
            [
                new CueTrack(1, "So Easy", null, 0),
                // INDEX 01 is where the song starts; the pregap before it belongs to the previous track.
                new CueTrack(2, "Eple", "Röyksopp feat. Someone", 252_000),
                new CueTrack(3, "Sparks", null, 470_493),
            ],
            file.Tracks);
    }

    [Fact]
    public void Decodes_latin1_cue_sheets_from_older_rippers()
    {
        var bytes = Encoding.Latin1.GetBytes("PERFORMER \"Björk\"\nFILE album.flac WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n");

        var cue = CueSheet.Parse(bytes)!;

        Assert.Equal("Björk", cue.Performer);
        Assert.Equal("album.flac", cue.Files[0].Name);
    }

    [Fact]
    public void Something_without_tracks_is_not_a_cue_sheet()
    {
        Assert.Null(CueSheet.Parse("REM COMMENT nothing here\n"));
    }
}
