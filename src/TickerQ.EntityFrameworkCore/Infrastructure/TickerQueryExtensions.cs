using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Infrastructure;


public static class TickerQueryExtensions
{
    public static IQueryable<TTimeTicker> WhereCanAcquire<TTimeTicker>(this IQueryable<TTimeTicker> q, string lockHolder) where TTimeTicker : TimeTickerEntity<TTimeTicker>
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