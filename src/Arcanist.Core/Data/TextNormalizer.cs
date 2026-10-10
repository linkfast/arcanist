using System.Globalization;
using System.Text;

namespace Arcanist.Core.Data;

/// <summary>
/// Builds the values stored in <c>Normalized*</c> columns: lowercase, without diacritics and with collapsed
/// whitespace. Lookups and searches compare these columns instead of relying on database collations, which
/// differ between SQLite (ASCII-only NOCASE) and PostgreSQL (case-sensitive).
/// </summary>
public static class TextNormalizer
{
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

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
