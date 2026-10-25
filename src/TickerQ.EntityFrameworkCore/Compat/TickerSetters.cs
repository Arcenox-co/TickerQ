using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
#if !NET10_0_OR_GREATER
using System.Reflection;
#endif

namespace TickerQ.EntityFrameworkCore.Compat;

/// <summary>
/// The column updates chosen by TickerQ's update helpers (see MappingExtensions), applied with the setter
/// API of the EF Core version in use. EF Core 10 accepts imperative setters (UpdateSettersBuilder); EF Core 8
/// and 9 need one SetPropertyCalls expression tree. The helpers decide which columns change once, for every
/// version; only applying them differs.
/// </summary>
internal sealed class TickerSetters<T>
{
#if NET10_0_OR_GREATER
    private readonly List<Action<UpdateSettersBuilder<T>>> _setters = [];
#else
    private readonly List<Func<Expression, Expression>> _setters = [];
#endif

    public TickerSetters<T> SetProperty<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
    {
#if NET10_0_OR_GREATER
        _setters.Add(builder => builder.SetProperty(property, value));
#else
        _setters.Add(calls => Expression.Call(calls, SetPropertyMethod<TProperty>.Value, property, Parameterized(value)));
#endif
        return this;
    }

#if NET10_0_OR_GREATER
    internal void ApplyTo(UpdateSettersBuilder<T> builder)
    {
        foreach (var setter in _setters)
            setter(builder);
    }
#else
    internal Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> ToExpression()
    {
        var calls = Expression.Parameter(typeof(SetPropertyCalls<T>), "setters");
        Expression body = calls;
        foreach (var setter in _setters)
            body = setter(body);
        return Expression.Lambda<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>(body, calls);
    }

    // A member read from a captured object is sent as a SQL parameter, as a C# closure would be; a bare
    // constant would be inlined into the statement text (and into EF's query cache key).
    private static Expression Parameterized<TProperty>(TProperty value)
        => Expression.Field(Expression.Constant(new Holder<TProperty>(value)), nameof(Holder<TProperty>.Value));

    private sealed class Holder<TProperty>(TProperty value)
    {
        public readonly TProperty Value = value;
    }

    // SetPropertyCalls<T>.SetProperty<TProperty>(Func<T, TProperty>, TProperty), taken from a compiled
    // expression instead of looked up by name.
    private static class SetPropertyMethod<TProperty>
    {
        public static readonly MethodInfo Value =
            ((MethodCallExpression)((Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>)(
                calls => calls.SetProperty(_ => default(TProperty)!, default(TProperty)!))).Body).Method;
    }
#endif
}

internal static class TickerSettersExtensions
{
    /// <summary>Runs ExecuteUpdateAsync with the setters <paramref name="configure"/> chooses.</summary>
    public static Task<int> ExecuteTickerUpdateAsync<T>(this IQueryable<T> source,
        Action<TickerSetters<T>> configure, CancellationToken cancellationToken)
    {
        var setters = new TickerSetters<T>();
        configure(setters);
#if NET10_0_OR_GREATER
        return source.ExecuteUpdateAsync(setters.ApplyTo, cancellationToken);
#else
        return source.ExecuteUpdateAsync(setters.ToExpression(), cancellationToken);
#endif
    }
}
