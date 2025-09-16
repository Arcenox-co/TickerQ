using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Infrastructure;


public static class TickerQueryExtensions
{
    public static IQueryable<TTimeTicker> WhereCanAcquire<TTimeTicker>(this IQueryable<TTimeTicker> q, string lockHolder) where TTimeTicker : TimeTickerEntity
    {
        Expression<Func<TTimeTicker, bool>> pred = e =>
            ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockHolder == lockHolder) || 
            ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockedAt == null);
           
        return q.Where(pred);
    }
    
    public static IQueryable<CronTickerOccurrenceEntity<TCronTicker>> WhereCanAcquire<TCronTicker>(this IQueryable<CronTickerOccurrenceEntity<TCronTicker>> q, string lockHolder) where TCronTicker : CronTickerEntity
    {
        return q.Where(e =>
                ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockHolder == lockHolder) || 
                ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockedAt == null)
            );
    }
}

public static class ExpressionHelper
{
    public static Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> CombineSetters<T>(
        Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> left,
        Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> right)
    {
        var replacer = new ParameterReplacer(right.Parameters[0], left.Body);
        var combined = replacer.Visit(right.Body);
        return Expression.Lambda<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>(combined, left.Parameters);
    }
}

public class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _parameter;
    private readonly Expression _replacement;

    public ParameterReplacer(ParameterExpression parameter, Expression replacement)
    {
        _parameter = parameter;
        _replacement = replacement;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        return node == _parameter ? _replacement : base.VisitParameter(node);
    }
}