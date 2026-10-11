using Arcanist.Core.Scanning;

namespace Arcanist.Core.Tests.Scanning;

public sealed class TagReaderTests
{
    [Theory]
    [InlineData("AC/DC", new[] { "AC/DC" })]
    [InlineData("Simon; Garfunkel", new[] { "Simon", "Garfunkel" })]
    [InlineData("  ", new string[0])]
    public void Splits_multiple_values_but_never_on_slashes(string value, string[] expected)
    {
        Assert.Equal(expected, TagReader.Split(value));
    }

    [Fact]
    public void Separates_featured_artists()
    {
        var (main, featured) = TagReader.SplitFeatured(["Daft Punk feat. Romanthony", "Artist ft. A & B", "Plain"]);

        Assert.Equal(["Daft Punk", "Artist", "Plain"], main);
        Assert.Equal(["Romanthony", "A", "B"], featured);
    }

    [Fact]
    public async Task Content_hash_ignores_tags()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcanist-hash-{Guid.NewGuid():N}.wav");
        try
        {
            AudioFixtures.WriteWav(path, 5, new FixtureTags("First"));
            var first = await HashAsync(path);

            AudioFixtures.Tag(path, new FixtureTags("A much longer title that changes the tag size", "Someone", WithCover: true));
            var second = await HashAsync(path);

            Assert.Equal(first, second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var metadata = TagReader.Read(stream, "audio/wav");
        Assert.True(metadata.AudioDataSize > 0);
        return await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, TestContext.Current.CancellationToken);
    }
}
