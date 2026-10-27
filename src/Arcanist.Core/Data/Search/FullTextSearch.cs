using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Data.Search;

/// <summary>What the full-text index covers.</summary>
public enum SearchKind
{
    Artists,
    Albums,
    Tracks,
    Playlists,
    MusicVideos,
}

/// <summary>A row that matched a search, with its relevance (higher is better).</summary>
public sealed class SearchHit
{
    public Guid Id { get; init; }

    public double Score { get; init; }
}

/// <summary>
/// Full-text search over the <c>SearchText</c> columns, with each provider's native index: FTS5 in SQLite and a
/// generated <c>tsvector</c> with a GIN index in PostgreSQL.
/// <para>
/// This is the one place with provider-specific SQL. The index objects live outside the EF model: they are created
/// (and checked) by <see cref="EnsureAsync"/> after migrations, so they come back by themselves if a future
/// migration ever rebuilds a table. Words match by prefix and all must be present; the text is normalized the way
/// <see cref="TextNormalizer"/> does, so accents and case never matter.
/// </para>
/// </summary>
public static class FullTextSearch
{
    private static readonly (SearchKind Kind, string Table)[] Tables =
    [
        (SearchKind.Artists, "Artists"),
        (SearchKind.Albums, "Albums"),
        (SearchKind.Tracks, "Tracks"),
        (SearchKind.Playlists, "Playlists"),
        (SearchKind.MusicVideos, "MusicVideos"),
    ];

    private const int MaxTerms = 8;
    private const int MaxTermLength = 64;

    /// <summary>Creates the index objects that are missing, rebuilding an index whose triggers were lost.</summary>
    public static async Task EnsureAsync(ArcanistDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlite())
        {
            foreach (var (_, table) in Tables)
                await EnsureSqliteAsync(db, table, cancellationToken);
        }
        else if (db.Database.IsNpgsql())
        {
            foreach (var (_, table) in Tables)
            {
#pragma warning disable EF1002 // Table names are constants, never user input.
                await db.Database.ExecuteSqlRawAsync(
                    $"""
                     ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "SearchVector" tsvector
                         GENERATED ALWAYS AS (to_tsvector('simple', coalesce("SearchText", ''))) STORED;
                     CREATE INDEX IF NOT EXISTS "IX_{table}_SearchVector" ON "{table}" USING GIN ("SearchVector");
                     """, cancellationToken);
#pragma warning restore EF1002
            }
        }
    }

    private static async Task EnsureSqliteAsync(ArcanistDbContext db, string table, CancellationToken cancellationToken)
    {
        var index = $"{table}_search";
        string[] triggers = [$"{index}_insert", $"{index}_delete", $"{index}_update"];

        var existing = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE name LIKE {0}", index + "%")
            .ToListAsync(cancellationToken);
        if (existing.Contains(index) && triggers.All(existing.Contains))
            return;

#pragma warning disable EF1002 // Table names are constants, never user input.
        // External content: the text stays in the table; the index holds only what it needs to find rows.
        await db.Database.ExecuteSqlRawAsync(
            $"""
             DROP TRIGGER IF EXISTS "{triggers[0]}";
             DROP TRIGGER IF EXISTS "{triggers[1]}";
             DROP TRIGGER IF EXISTS "{triggers[2]}";
             DROP TABLE IF EXISTS "{index}";
             CREATE VIRTUAL TABLE "{index}" USING fts5("SearchText", content='{table}', content_rowid='rowid', tokenize='unicode61');
             CREATE TRIGGER "{triggers[0]}" AFTER INSERT ON "{table}" WHEN new."SearchText" IS NOT NULL BEGIN
                 INSERT INTO "{index}"(rowid, "SearchText") VALUES (new.rowid, new."SearchText");
             END;
             CREATE TRIGGER "{triggers[1]}" AFTER DELETE ON "{table}" WHEN old."SearchText" IS NOT NULL BEGIN
                 INSERT INTO "{index}"("{index}", rowid, "SearchText") VALUES ('delete', old.rowid, old."SearchText");
             END;
             CREATE TRIGGER "{triggers[2]}" AFTER UPDATE OF "SearchText" ON "{table}" BEGIN
                 INSERT INTO "{index}"("{index}", rowid, "SearchText") SELECT 'delete', old.rowid, old."SearchText" WHERE old."SearchText" IS NOT NULL;
                 INSERT INTO "{index}"(rowid, "SearchText") SELECT new.rowid, new."SearchText" WHERE new."SearchText" IS NOT NULL;
             END;
             INSERT INTO "{index}"("{index}") VALUES ('rebuild');
             """, cancellationToken);
#pragma warning restore EF1002
    }

    /// <summary>
    /// Rows of <paramref name="kind"/> containing every word of <paramref name="query"/> (as word prefixes); null
    /// when the query has no words. Compose it into other queries: it is a subquery, not a list.
    /// </summary>
    public static IQueryable<SearchHit>? Match(ArcanistDbContext db, SearchKind kind, string? query)
    {
        var terms = Terms(query);
        if (terms.Count == 0)
            return null;

        var table = Tables.Single(t => t.Kind == kind).Table;
#pragma warning disable EF1002 // Table names are constants; the search terms are parameters.
        if (db.Database.IsNpgsql())
        {
            var tsQuery = string.Join(" & ", terms.Select(t => t + ":*"));
            return db.Database.SqlQueryRaw<SearchHit>(
                $$"""SELECT t."Id", ts_rank(t."SearchVector", q)::float8 AS "Score" FROM "{{table}}" t, to_tsquery('simple', {0}) q WHERE t."SearchVector" @@ q""",
                tsQuery);
        }

        // FTS5 query syntax: quoted words, each a prefix ("word"*), all required. bm25 is lower for better matches.
        var match = string.Join(' ', terms.Select(t => $"\"{t}\"*"));
        return db.Database.SqlQueryRaw<SearchHit>(
            $$"""SELECT t."Id" AS "Id", -bm25("{{table}}_search") AS "Score" FROM "{{table}}_search" JOIN "{{table}}" t ON t.rowid = "{{table}}_search".rowid WHERE "{{table}}_search" MATCH {0}""",
            match);
#pragma warning restore EF1002
    }

    /// <summary>The words of a query, normalized like the indexed text and safe for both query syntaxes.</summary>
    internal static IReadOnlyList<string> Terms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var normalized = TextNormalizer.Normalize(query);
        var terms = new List<string>();
        var word = new StringBuilder();
        foreach (var c in normalized.Append(' '))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }

            if (word.Length > 0 && terms.Count < MaxTerms)
                terms.Add(word.ToString()[..Math.Min(word.Length, MaxTermLength)]);
            word.Clear();
        }

        return terms.Distinct().ToList();
    }

    /// <summary>The text indexed for a row: normalized words, separated by spaces, within the column's length.</summary>
    public static string Text(params IEnumerable<string?> parts)
    {
        var text = string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => TextNormalizer.Normalize(p!)));
        return text.Length <= ArcanistDbContext.SearchTextLength ? text : text[..ArcanistDbContext.SearchTextLength];
    }
}
