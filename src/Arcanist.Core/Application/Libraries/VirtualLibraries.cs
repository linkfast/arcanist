namespace Arcanist.Core.Application.Libraries;

/// <summary>
/// Libraries that are views, not folders. Today one: "All" (<see cref="All"/>), every library the user can access
/// at once, for apps that add an Arcanist library of their own (Ultimate Desktop). Nothing is uploaded or migrated
/// to it: apps add it as one more Arcanist library.
/// </summary>
public static class VirtualLibraries
{
    /// <summary>The id of "All", the same on every server.</summary>
    public static readonly Guid All = new("a11a11a1-0000-4000-8000-000000000001");

    public static bool IsVirtual(Guid? id) => id == All;

    /// <summary>A library filter as the catalog applies it: "All" is no filter at all.</summary>
    public static Guid? Filter(Guid? libraryId) => IsVirtual(libraryId) ? null : libraryId;

    /// <summary>Its name in the first of <paramref name="languages"/> that has one, otherwise in English.</summary>
    public static string AllName(IEnumerable<string> languages) =>
        languages.Select(tag => tag.Split('-')[0].ToLowerInvariant()).FirstOrDefault(tag => tag is "es" or "en") == "es" ? "Todo" : "All";
}
