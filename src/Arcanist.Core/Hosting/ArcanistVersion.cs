using System.Reflection;

namespace Arcanist.Core.Hosting;

/// <summary>This server's version, and comparing versions as Semantic Versioning 2.0 orders them.</summary>
public static class ArcanistVersion
{
    /// <summary>The running version, such as <c>0.1.0-alpha.1</c> (without build metadata).</summary>
    public static readonly string Current =
        typeof(ArcanistVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";

    /// <summary>Whether <paramref name="version"/> is a pre-release (it has a suffix: 0.2.0-beta.1).</summary>
    public static bool IsPrerelease(string version) => Parse(version) is { Prerelease.Length: > 0 };

    /// <summary>
    /// Orders two versions (a leading "v" is ignored): negative when <paramref name="a"/> comes first. A version
    /// that cannot be read comes before any that can.
    /// </summary>
    public static int Compare(string a, string b)
    {
        var (x, y) = (Parse(a), Parse(b));
        if (x is null || y is null)
            return (x is null ? 0 : 1) - (y is null ? 0 : 1);
        for (var i = 0; i < 3; i++)
        {
            if (x.Numbers[i] != y.Numbers[i])
                return x.Numbers[i].CompareTo(y.Numbers[i]);
        }

        // A release comes after its pre-releases; pre-releases compare identifier by identifier.
        if (x.Prerelease.Length == 0 || y.Prerelease.Length == 0)
            return (x.Prerelease.Length == 0 ? 1 : 0) - (y.Prerelease.Length == 0 ? 1 : 0);
        for (var i = 0; i < Math.Min(x.Prerelease.Length, y.Prerelease.Length); i++)
        {
            var (p, q) = (x.Prerelease[i], y.Prerelease[i]);
            var (pNumeric, qNumeric) = (long.TryParse(p, out var pn), long.TryParse(q, out var qn));
            var order = (pNumeric, qNumeric) switch
            {
                (true, true) => pn.CompareTo(qn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(p, q),
            };
            if (order != 0)
                return order;
        }

        return x.Prerelease.Length.CompareTo(y.Prerelease.Length);
    }

    public static bool TryNormalize(string? version, out string normalized)
    {
        normalized = "";
        if (version is null || Parse(version) is null)
            return false;
        normalized = version.Trim().TrimStart('v', 'V').Split('+')[0];
        return true;
    }

    private sealed record Parsed(long[] Numbers, string[] Prerelease);

    private static Parsed? Parse(string version)
    {
        var text = version.Trim().TrimStart('v', 'V').Split('+')[0];
        var dash = text.IndexOf('-');
        var core = dash < 0 ? text : text[..dash];
        var parts = core.Split('.');
        if (parts.Length != 3)
            return null;
        var numbers = new long[3];
        for (var i = 0; i < 3; i++)
        {
            if (!long.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out numbers[i]))
                return null;
        }

        var prerelease = dash < 0 ? [] : text[(dash + 1)..].Split('.');
        return prerelease.Any(string.IsNullOrEmpty) ? null : new Parsed(numbers, prerelease);
    }
}
