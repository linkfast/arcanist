namespace Arcanist.Core.Application.Catalog;

public static class MusicalKeys
{
    private static readonly string[] Major = ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];
    private static readonly string[] Minor = ["Cm", "C#m", "Dm", "Ebm", "Em", "Fm", "F#m", "Gm", "G#m", "Am", "Bbm", "Bm"];

    public static MusicalKey Create(int tonic, bool minor, double confidence)
    {
        tonic = (tonic % 12 + 12) % 12;
        // The Camelot wheel steps by fifths: C major is 8B, and a minor key shares the number of its relative major.
        var major = minor ? (tonic + 3) % 12 : tonic;
        var number = (major * 7 % 12 + 7) % 12 + 1;
        return new MusicalKey(
            tonic,
            minor ? KeyMode.Minor : KeyMode.Major,
            minor ? Minor[tonic] : Major[tonic],
            $"{number}{(minor ? 'A' : 'B')}",
            Math.Round(confidence, 2));
    }
}
