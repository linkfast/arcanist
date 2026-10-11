using System.Linq.Expressions;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// Keyset pagination ordered by a string key with the id as tie-breaker. Unlike offsets, pages stay correct while
/// scans insert rows, and late pages cost the same as the first.
/// </summary>
internal static class Keyset
{
    public static async Task<(List<T> Page, string? Next, int Total)> PageAsync<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> keySelector,
        string? after,
        int? first,
        CancellationToken cancellationToken)
        where T : Entity
    {
        var size = Math.Clamp(first ?? CatalogService.DefaultPageSize, 1, CatalogService.MaxPageSize);
        var total = await query.CountAsync(cancellationToken);
        var key = keySelector.Compile();

        var ordered = query.AsNoTracking();
        if (after is not null)
        {
            var (afterKey, afterId) = Cursor.Decode(after);
            ordered = ordered.Where(After(keySelector, afterKey, afterId));
        }

        var items = await ordered
            .OrderBy(keySelector).ThenBy(e => e.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);

        if (items.Count <= size)
            return (items, null, total);

        items.RemoveAt(size);
        var last = items[^1];
        return (items, Cursor.Encode(key(last), last.Id), total);
    }

    /// <summary>Builds <c>key &gt; afterKey || (key == afterKey &amp;&amp; id &gt; afterId)</c> for the given key.</summary>
    public static Expression<Func<T, bool>> After<T>(
        Expression<Func<T, string>> keySelector, string afterKey, Guid afterId)
        where T : Entity
    {
        var entity = keySelector.Parameters[0];
        var key = keySelector.Body;
        var compare = Expression.Call(
            typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!,
            key,
            Expression.Constant(afterKey));
        var zero = Expression.Constant(0);
        var id = Expression.Property(entity, nameof(Entity.Id));

        var body = Expression.OrElse(
            Expression.GreaterThan(compare, zero),
            Expression.AndAlso(
                Expression.Equal(compare, zero),
                Expression.GreaterThan(
                    Expression.Call(id, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, Expression.Constant(afterId)),
                    zero)));

        return Expression.Lambda<Func<T, bool>>(body, entity);
    }
}
