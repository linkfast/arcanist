using System.Globalization;
using System.Text;

namespace Arcanist.Core.Data;

/// <summary>
/// Builds the values stored in <c>Normalized*</c> columns: lowercase, without diacritics and with collapsed
/// whitespace. Lookups and searches compare these columns instead of relying on database collations, which
/// differ between SQLite (ASCII-only NOCASE) and PostgreSQL (case-sensitive). Letters that are not accented
/// forms but are commonly typed as plain Latin ("æ", "ø", "ß"…) are folded too, so "agaetis" finds "Ágætis".
/// Changing these rules after release needs a migration that recomputes every stored <c>Normalized*</c> value.
/// </summary>
public static class TextNormalizer
{
    private static readonly Dictionary<char, string> Foldings = new()
    {
        ['æ'] = "ae", ['œ'] = "oe", ['ø'] = "o", ['ß'] = "ss", ['ð'] = "d", ['þ'] = "th",
        ['ł'] = "l", ['đ'] = "d", ['ħ'] = "h", ['ı'] = "i", ['ŧ'] = "t",
    };

    /// <summary>
    /// Fails at startup when Unicode normalization is unavailable. On Linux it needs ICU; in globalization-invariant
    /// mode (no libicu, or <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c>) .NET silently returns strings unchanged, and
    /// the stored values would stop ignoring accents.
    /// </summary>
    public static void EnsureSupported()
    {
        if ("é".Normalize(NormalizationForm.FormD).Length != 2)
            throw new PlatformNotSupportedException(
                "Arcanist needs the ICU library (libicu) for Unicode normalization. Install it, and do not set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.");
    }

    public static string Normalize(string value)
    {
        // Compatibility decomposition also folds ligatures and full-width forms ("ﬁ" → "fi", "Ａ" → "A").
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            var lower = char.ToLowerInvariant(c);
            if (Foldings.TryGetValue(lower, out var folded))
                builder.Append(folded);
            else
                builder.Append(lower);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
