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

    /// <summary>
    /// <see cref="Contains{TEntity,TValue}"/> for hot paths: the same scalar equality tree, but each
    /// value is read from a holder EF extracts as an ordinary scalar parameter, and the list is padded
    /// (repeating its last value) to a power of two. A tree of a given size therefore compiles once
    /// and reuses one cached query and database plan, instead of one per distinct set of values.
    /// </summary>
    public static Expression<Func<TEntity, bool>> ContainsAsParameters<TEntity, TValue>(
        IReadOnlyList<TValue> values,
        Expression<Func<TEntity, TValue>> memberSelector)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(memberSelector);
        if (values.Count == 0) return _ => false;

        var slots = ParameterSlots<TValue>.Padded(values);
        return Expression.Lambda<Func<TEntity, bool>>(
            OrTree(slots.Values.Length, i => Expression.Equal(memberSelector.Body, slots.Slot(i))),
            memberSelector.Parameters);
    }

    /// <summary>Balanced OR of <paramref name="count"/> terms, so deep lists stay shallow trees.</summary>
    internal static Expression OrTree(int count, Func<int, Expression> term)
    {
        Expression Build(int start, int length)
        {
            if (length == 1) return term(start);
            var left = length / 2;
            return Expression.OrElse(Build(start, left), Build(start + left, length - left));
        }

        return Build(0, count);
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

/// <summary>
/// Values a predicate reads through <see cref="Slot"/>: EF evaluates <c>holder.Values[i]</c> into a
/// scalar query parameter, keeping the values out of the query's cache key and its SQL text.
/// </summary>
internal sealed class ParameterSlots<TValue>
{
    public TValue[] Values;

    internal static ParameterSlots<TValue> Padded(IReadOnlyList<TValue> values)
    {
        var size = values.Count <= 1 ? 1 : (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)values.Count);
        var padded = new TValue[size];
        for (var i = 0; i < size; i++) padded[i] = values[Math.Min(i, values.Count - 1)];
        return new ParameterSlots<TValue> { Values = padded };
    }

    internal Expression Slot(int index) => Expression.ArrayIndex(
        Expression.Field(Expression.Constant(this), nameof(Values)), Expression.Constant(index));
}
