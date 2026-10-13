namespace Arcanist.Core.Extensions;

/// <summary>The extensions that ship inside the server, built on the same SDK as everyone else's.</summary>
internal static class BuiltInExtensions
{
    public static IReadOnlyList<IBuiltInExtension> All { get; } = [new Subsonic.SubsonicExtension()];
}
