using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Builds scalar equality predicates for runtime collections. This deliberately avoids
/// EF's primitive-collection parameter translation because some relational providers
/// cannot type-map those parameters consistently.
/// </summary>
internal static class EfPredicateBuilder
{
    public static Expression<Func<TEntity, bool>> Contains<TEntity, TValue>(
        IReadOnlyList<TValue> values,
        Expression<Func<TEntity, TValue>> memberSelector)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(memberSelector);
        if (values.Count == 0) return _ => false;

        Expression Build(int start, int length)
        {
            if (length == 1)
                return Expression.Equal(
                    memberSelector.Body,
                    Expression.Constant(values[start], typeof(TValue)));

            var leftLength = length / 2;
            return Expression.OrElse(
                Build(start, leftLength),
                Build(start + leftLength, length - leftLength));
        }

        return Expression.Lambda<Func<TEntity, bool>>(
            Build(0, values.Count),
            memberSelector.Parameters);
    }

    public static Expression<Func<TEntity, bool>> DoesNotContain<TEntity, TValue>(
        IReadOnlyList<TValue> values,
        Expression<Func<TEntity, TValue>> memberSelector)
    {
        var predicate = Contains(values, memberSelector);
        return Expression.Lambda<Func<TEntity, bool>>(
            Expression.Not(predicate.Body),
            predicate.Parameters);
    }
}
