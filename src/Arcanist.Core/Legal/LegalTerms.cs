using System.Reflection;
using System.Text.Json;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Extensions;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Legal;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Who runs this server: its administrator (self-hosted), or LinkFast S.A. as a service.</summary>
public enum LegalMode
{
    /// <summary>Installed and run by its administrator: the software as is, bugs as GitHub issues.</summary>
    SelfHosted,

    /// <summary>Part of LinkFast S.A.'s service (its managed storage extension runs): support and warranties of its contract.</summary>
    LinkfastService,
}

/// <summary>Whom the terms are shown to: the administrator setting up the server, or a user of a player.</summary>
public enum LegalAudience
{
    Admin,
    User,
}

public sealed record LegalSection(string Id, string Title, IReadOnlyList<string> Paragraphs);

/// <summary>The terms and conditions and the disclaimer, in a language, for this server's mode.</summary>
/// <param name="Version">Changes when the text changes: accept it again.</param>
/// <param name="IssuesUrl">Where bugs are reported (the project's GitHub issues).</param>
/// <param name="Accept">The sentence of the box to tick.</param>
public sealed record LegalTerms(
    string Version,
    LegalMode Mode,
    string Language,
    string Title,
    IReadOnlyList<LegalSection> Sections,
    string DisclaimerTitle,
    IReadOnlyList<LegalSection> Disclaimer,
    string Accept,
    string IssuesUrl);

/// <summary>When and by whom the terms were accepted while setting the server up.</summary>
internal sealed record LegalAcceptance(string Version, DateTime AcceptedAt, string UserName);

/// <summary>
/// The terms and conditions and the disclaimer (<c>Legal/terms.json</c>, embedded), which the setup wizard and the
/// players ask to accept. Anyone can read them, before setup too.
/// </summary>
public sealed class LegalService(ExtensionHost extensions, IOptionsMonitor<ServerOptions> options)
{
    /// <summary>Extensions of LinkFast S.A.'s service: while one runs, the server is part of it.</summary>
    public static readonly string[] ServiceExtensions = ["arcanist-cloud-storage"];

    private static readonly Lazy<Document> Texts = new(Load);

    /// <summary>The version the setup wizard must send to complete.</summary>
    public static string CurrentVersion => Texts.Value.Version;

    public LegalMode Mode =>
        ServiceExtensions.Any(id => extensions.Find(id) is { Status: ExtensionStatus.Running } && extensions.IsEnabled(id))
            ? LegalMode.LinkfastService
            : LegalMode.SelfHosted;

    public LegalTerms Get(string? language, LegalAudience audience)
    {
        var document = Texts.Value;
        var code = language?.Split('-')[0].ToLowerInvariant() is { } wanted && document.Documents.ContainsKey(wanted) ? wanted : "en";
        var text = document.Documents[code];
        var mode = Mode;
        var issues = IssuesUrl();
        var modeName = mode == LegalMode.SelfHosted ? "SELF_HOSTED" : "LINKFAST_SERVICE";
        var audienceName = audience == LegalAudience.Admin ? "ADMIN" : "USER";

        LegalSection Section(Part part) =>
            new(part.Id, part.Title, part.Paragraphs.Select(p => p.Replace("{issuesUrl}", issues, StringComparison.Ordinal)).ToList());

        return new LegalTerms(
            document.Version,
            mode,
            code,
            text.Title,
            text.Sections.Where(s => (s.Modes is null || s.Modes.Contains(modeName)) && (s.Audiences is null || s.Audiences.Contains(audienceName)))
                .Select(Section).ToList(),
            text.DisclaimerTitle,
            text.Disclaimer.Select(Section).ToList(),
            text.Accept,
            issues);
    }

    /// <summary>The issues of the repository the source comes from (GitHub), or the project's.</summary>
    private string IssuesUrl()
    {
        var source = (options.CurrentValue.SourceUrl ?? "https://github.com/linkfast/arcanist").TrimEnd('/');
        if (source.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            source = source[..^4];
        return source.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ? source + "/issues" : "https://github.com/linkfast/arcanist/issues";
    }

    private static Document Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Arcanist.Core.Legal.terms.json")
                           ?? throw new InvalidOperationException("The legal texts are missing from the build.");
        return JsonSerializer.Deserialize<Document>(stream, JsonSerializerOptions.Web)!;
    }

    private sealed record Document(string Version, Dictionary<string, Language> Documents);

    private sealed record Language(string Title, List<Part> Sections, string DisclaimerTitle, List<Part> Disclaimer, string Accept);

    private sealed record Part(string Id, string Title, List<string> Paragraphs, List<string>? Modes = null, List<string>? Audiences = null);
}
