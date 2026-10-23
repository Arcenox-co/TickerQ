#nullable disable
using System.Linq.Expressions;
using TickerQ.Utilities.Infrastructure;
using System.Runtime.CompilerServices;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>
{
    private async IAsyncEnumerable<TEntity> ScanQueryAsync<TEntity>(string idsKey, Func<Guid, string> keyBuilder,
        IReadOnlyList<Expression<Func<TEntity, bool>>> predicates,
        [EnumeratorCancellation] CancellationToken cancellationToken) where TEntity : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await IsActivationPublicationVisibleAsync(cancellationToken).ConfigureAwait(false)) yield break;
        foreach (var predicate in predicates)
        {
            if (!TryFindGuidEquality(predicate.Body, predicate.Parameters[0], "Id", out var id)) continue;
            if (!await Db.SetContainsAsync(idsKey, id.ToString()).ConfigureAwait(false)) yield break;
            cancellationToken.ThrowIfCancellationRequested();
            var row = await Serializer.GetAsync<TEntity>(keyBuilder(id)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (row != null) yield return row;
            yield break;
        }

        await foreach (var row in Serializer.ScanSetAsync<TEntity>(idsKey, keyBuilder, cancellationToken)
                           .ConfigureAwait(false))
            yield return row;
    }

    private async IAsyncEnumerable<TTimeTicker> ScanMonitoringQueryAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Preserve monitoring's global identity de-duplication while retaining only IDs,
        // rather than retaining every full aggregate in a dictionary.
        var seen = new HashSet<Guid>();
        await foreach (var root in ScanQueryAsync<TTimeTicker>(TimeTickerIdsKey, TimeTickerKey, [], cancellationToken)
                           .ConfigureAwait(false))
        {
            var pending = new Stack<TTimeTicker>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = pending.Pop();
                if (!seen.Add(row.Id)) continue;
                yield return row;
                foreach (var child in row.Children ?? Array.Empty<TTimeTicker>()) pending.Push(child);
            }
        }
    }

    private static bool TryFindGuidEquality(Expression expression, ParameterExpression parameter,
        string property, out Guid id)
    {
        if (expression is BinaryExpression { NodeType: ExpressionType.AndAlso } and)
            return TryFindGuidEquality(and.Left, parameter, property, out id) ||
                   TryFindGuidEquality(and.Right, parameter, property, out id);
        if (expression is BinaryExpression { NodeType: ExpressionType.Equal } equal)
        {
            if (IsProperty(equal.Left, parameter, property) && TryReadGuid(equal.Right, out id)) return true;
            if (IsProperty(equal.Right, parameter, property) && TryReadGuid(equal.Left, out id)) return true;
        }
        id = default;
        return false;
    }

    private static bool IsProperty(Expression expression, ParameterExpression parameter, string property)
        => expression is MemberExpression member && member.Expression == parameter && member.Member.Name == property;

    private static bool TryReadGuid(Expression expression, out Guid id)
    {
        // Only captured values/constants are index candidates. Leave method calls and
        // expressions involving the row to the authoritative compiled predicate.
        var current = expression;
        while (current is MemberExpression member) current = member.Expression;
        if (current is ConstantExpression && expression.Type == typeof(Guid))
        {
            id = Expression.Lambda<Func<Guid>>(expression).CompileAotSafe()();
            return true;
        }
        id = default;
        return false;
    }
}
