namespace Arcanist.Core.Hosting;

/// <summary>
/// Open source or not: extensions and web players say their license in their manifest (an SPDX id or expression).
/// Built-in extensions are Arcanist's own (AGPL); a component without a license, or with one of its own
/// (<c>LicenseRef-…</c>), is closed source. With the 100% open source mode on (<see cref="ModeKey"/>, in
/// <c>arcanist.json</c> so it is known before extensions load), closed source components are neither installed nor
/// run.
/// </summary>
public static class OpenSource
{
    public const string ModeKey = "Arcanist:OpenSourceOnly";

    /// <summary>Open source licenses (SPDX ids), the same the marketplace accepts as open source.</summary>
    public static readonly IReadOnlySet<string> Licenses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MIT", "MIT-0", "ISC", "0BSD", "Unlicense", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "MPL-2.0", "Zlib",
        "LGPL-2.1-only", "LGPL-2.1-or-later", "LGPL-3.0-only", "LGPL-3.0-or-later", "GPL-2.0-or-later", "GPL-3.0-only",
        "GPL-3.0-or-later", "AGPL-3.0-only", "AGPL-3.0-or-later", "EUPL-1.2", "CC0-1.0",
    };

    public static bool IsOn(IConfiguration configuration) => configuration.GetValue<bool>(ModeKey);

    /// <summary>
    /// Whether an SPDX license (or expression) is open source: every license in it, for <c>AND</c>; any, for
    /// <c>OR</c>. No license is not open source.
    /// </summary>
    public static bool IsOpenLicense(string? license)
    {
        if (string.IsNullOrWhiteSpace(license))
            return false;
        // A note after the expression, as in "AGPL-3.0-or-later (Webamp: MIT)": the expression is what counts.
        var noted = System.Text.RegularExpressions.Regex.Match(license.Trim(), @"^(?<expression>.+?)\s+\((?<note>[^()]*)\)$");
        if (noted.Success && !noted.Groups["note"].Value.Contains(" OR ") && !noted.Groups["note"].Value.Contains(" AND "))
            license = noted.Groups["expression"].Value;
        var text = license.Replace("(", " ").Replace(")", " ");
        var alternatives = text.Split(" OR ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return alternatives.Any(alternative => alternative
            .Split(" AND ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // "GPL-3.0-or-later WITH Classpath-exception-2.0": the exception only adds permissions.
            .Select(part => part.Split(" WITH ", StringSplitOptions.TrimEntries)[0])
            .All(Licenses.Contains));
    }
}
