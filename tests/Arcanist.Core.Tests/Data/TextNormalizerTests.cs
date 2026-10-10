using Arcanist.Core.Data;

namespace Arcanist.Core.Tests.Data;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Beyoncé", "beyonce")]
    [InlineData("  Sigur   Rós ", "sigur ros")]
    [InlineData("MOTÖRHEAD", "motorhead")]
    [InlineData("ﬁve", "five")]
    [InlineData("ＡＢＣ", "abc")]
    [InlineData("AC/DC", "ac/dc")]
    [InlineData("坂本龍一", "坂本龍一")]
    [InlineData("", "")]
    public void Normalizes_case_diacritics_and_whitespace(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }
}
