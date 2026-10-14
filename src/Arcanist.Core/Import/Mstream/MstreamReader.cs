using Arcanist.Core.Application;
using Arcanist.Core.Application.Import;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Import.Mstream;

/// <summary>An mStream installation read for importing, from its folder or from its server.</summary>
/// <param name="Location">The folder, or the server's address.</param>
/// <param name="Version">5 or 6 when read from its folder; null through the API, which does not tell.</param>
/// <param name="Content">Each user's data; through the API it arrives when they first sign in.</param>
internal sealed record MstreamSource(
    MstreamImportKind Kind,
    string Location,
    int? Version,
    IReadOnlyList<MstreamLibraryData> Libraries,
    IReadOnlyList<MstreamUserData> Users,
    IReadOnlyList<MstreamUserContent> Content);

/// <summary>Reads mStream installations for the importer and for the setup wizard, which has no database yet.</summary>
internal sealed class MstreamReader(MstreamServerClient client)
{
    /// <summary>Reads the installation at <paramref name="folder"/>, or the <paramref name="server"/> (exactly one).</summary>
    public async Task<MstreamSource> ReadAsync(string? folder, MstreamServerInput? server, CancellationToken cancellationToken)
    {
        if ((folder is null) == (server is null))
            throw ArcanistException.Field("folder", ErrorCodes.Required);

        if (folder is not null)
        {
            var data = MstreamFolder.Read(folder);
            return new MstreamSource(MstreamImportKind.Folder, folder.Trim(), data.Version, data.Libraries, data.Users, data.Content.Values.ToList());
        }

        var url = MstreamServerClient.ParseUrl(server!.Url) ?? throw ArcanistException.Field("url", ErrorCodes.InvalidFormat);
        var token = await client.LoginAsync(url, server.UserName ?? "", server.Password ?? "", cancellationToken)
                    ?? throw ArcanistException.Field("password", ErrorCodes.InvalidCredentials);
        var read = await client.ReadAsync(url, token, cancellationToken);
        return new MstreamSource(MstreamImportKind.Server, url.AbsoluteUri.TrimEnd('/'), null, read.Libraries, read.Users, []);
    }

    /// <summary>What the installation holds, matched against this server's library folders and user names.</summary>
    public static MstreamAnalysis Analyze(
        MstreamSource source, IReadOnlyList<(Guid Id, string Location)> librarySources, IReadOnlyCollection<string> normalizedUserNames)
    {
        var server = source.Kind == MstreamImportKind.Server;
        return new MstreamAnalysis(
            source.Version,
            source.Libraries.Select(l => new MstreamLibraryInfo(l.Name, l.Root, l.Root.Length > 0 && Directory.Exists(l.Root),
                librarySources.Where(s => SamePath(s.Location, l.Root)).Select(s => (Guid?)s.Id).FirstOrDefault())).ToList(),
            source.Users.Select(u => new MstreamUserInfo(u.Name, u.IsAdmin, u.Vpaths, normalizedUserNames.Contains(TextNormalizer.Normalize(u.Name)),
                server || u is { PasswordHash.Length: > 0, Salt.Length: > 0 })).ToList(),
            source.Content.Sum(c => c.Playlists.Count),
            source.Content.Sum(c => c.Tracks.Count(t => t.Rating > 0)),
            source.Content.Sum(c => c.Tracks.Count(t => t.PlayCount > 0)));
    }

    /// <summary>Whether two folders are the same, however they are written (separators, trailing slash, case on Windows).</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string path)
    {
        path = path.Trim();
        try
        {
            if (Path.IsPathFullyQualified(path))
                path = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return Path.TrimEndingDirectorySeparator(path);
    }
}
