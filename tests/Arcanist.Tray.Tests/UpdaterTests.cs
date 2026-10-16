namespace Arcanist.Tray.Tests;

public sealed class UpdaterTests
{
    [Theory]
    [InlineData("0.1.0-alpha.1", true)]
    [InlineData("1.0.0-rc.2+abc", true)]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0+build-7", false)]
    [InlineData(null, false)]
    public void Pre_releases_follow_pre_releases_and_stable_versions_do_not(string? version, bool expected) =>
        Assert.Equal(expected, Updater.FollowsPrereleases(version));
}
