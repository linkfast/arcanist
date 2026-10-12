using Arcanist.Core.Application;
using Arcanist.Core.Organization;

namespace Arcanist.Core.Tests.Organization;

public sealed class PathTemplateTests
{
    private static readonly PathValues Song = new("Sigur Rós", "Sigur Rós", "Ágætis byrjun", 1999, Disc: 1, MultiDisc: false, Track: 3, "Starálfur", "Post-rock");

    [Fact]
    public void Default_template_names_album_folders_by_year_and_files_by_number()
    {
        Assert.Equal("Sigur Rós/1999 - Ágætis byrjun/03 Starálfur", PathTemplate.DefaultTemplate.Render(Song));
    }

    [Fact]
    public void Optional_sections_disappear_when_a_value_is_missing()
    {
        var template = PathTemplate.DefaultTemplate;

        Assert.Equal("Sigur Rós/Ágætis byrjun/Starálfur", template.Render(Song with { Year = null, Track = null }));
        // The disc only appears on albums of several discs.
        Assert.Equal("Sigur Rós/1999 - Ágætis byrjun/2-03 Starálfur", template.Render(Song with { Disc = 2, MultiDisc = true }));
        Assert.Equal("Sigur Rós/1999 - Ágætis byrjun/03 Starálfur", template.Render(Song with { Disc = 2, MultiDisc = false }));
    }

    [Fact]
    public void Missing_artist_and_album_have_names_of_their_own()
    {
        var values = new PathValues(null, null, null, null, null, false, null, "Untitled", null);

        Assert.Equal("Unknown Artist/Unknown Album/Untitled", PathTemplate.DefaultTemplate.Render(values));
        Assert.Equal("Solo/Unknown Album/Untitled", PathTemplate.DefaultTemplate.Render(values with { Artist = "Solo" }));
    }

    [Theory]
    [InlineData("AC/DC", "AC_DC")]
    [InlineData("Title: Subtitle", "Title - Subtitle")]
    [InlineData("What?", "What")]
    [InlineData("Say \"Hi\"", "Say 'Hi'")]
    [InlineData("...And Justice for All", "And Justice for All")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("  lots   of\tspace ", "lots of space")]
    [InlineData("CON", "CON_")]
    [InlineData("nul.txt", "nul.txt_")]
    [InlineData("???", "_")]
    public void Names_are_valid_on_every_file_system(string value, string expected)
    {
        Assert.Equal(expected, PathTemplate.SanitizeName(value));
    }

    [Fact]
    public void Long_names_are_cut_without_splitting_characters()
    {
        var name = PathTemplate.SanitizeName(string.Concat(Enumerable.Repeat("é🎵", 200)));

        Assert.True(name.Length <= 150);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name) <= 230);
        Assert.False(char.IsHighSurrogate(name[^1]));
    }

    [Fact]
    public void Custom_templates_use_any_placeholder()
    {
        var template = PathTemplate.Parse("{genre}/{artist} - {album}/{track}. {title}");

        Assert.Equal("Post-rock/Sigur Rós - Ágætis byrjun/03. Starálfur", template.Render(Song));
        Assert.Equal("Post-rock/Sigur Rós - Ágætis byrjun", template.RenderFolder(Song));
    }

    [Theory]
    [InlineData("", ErrorCodes.Required)]
    [InlineData("{albumartist}/{album}/{track}", ErrorCodes.TemplateTitleRequired)]
    [InlineData("{title}/{album}", ErrorCodes.TemplateTitleRequired)]
    [InlineData("{albumartist}/{composer}/{title}", ErrorCodes.TemplateUnknownPlaceholder)]
    [InlineData("{albumartist}//{title}", ErrorCodes.InvalidFormat)]
    [InlineData("{albumartist}/<{year} - {title}", ErrorCodes.InvalidFormat)]
    [InlineData("{albumartist}/<<{year}>>{title}", ErrorCodes.InvalidFormat)]
    [InlineData("{albumartist/{title}", ErrorCodes.InvalidFormat)]
    public void Invalid_templates_are_rejected_with_a_reason(string text, string error)
    {
        Assert.False(PathTemplate.TryParse(text, out _, out var reported));
        Assert.Equal(error, reported);
    }

    [Fact]
    public void Backslashes_separate_folders_too()
    {
        Assert.Equal("Sigur Rós/Starálfur", PathTemplate.Parse(@"{albumartist}\{title}").Render(Song));
    }
}
