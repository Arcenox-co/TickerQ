using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace TickerQ.Utilities.Infrastructure;

internal static class ScalarMembershipExpression
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
}
