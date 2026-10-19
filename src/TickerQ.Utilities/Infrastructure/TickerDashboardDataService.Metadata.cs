using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities.Infrastructure;

public sealed partial class TickerDashboardDataService<TTimeTicker, TCronTicker>
{
    // Explicit projections prevent request payloads, user-defined large columns and
    // navigation graphs from crossing the database boundary for dashboard reads.
    private static readonly Expression<Func<TTimeTicker, TTimeTicker>> TimeMetadata = e => new TTimeTicker
    {
        Id = e.Id, Function = e.Function, Description = e.Description,
        CreatedAt = e.CreatedAt, UpdatedAt = e.UpdatedAt,
        Status = e.Status, ExecutionTime = e.ExecutionTime, ElapsedTime = e.ElapsedTime,
        Retries = e.Retries, RetryCount = e.RetryCount, RetryIntervals = e.RetryIntervals,
        ExceptionMessage = e.ExceptionMessage, SkippedReason = e.SkippedReason,
        ParentId = e.ParentId, ChainRootId = e.ChainRootId, OriginCronTickerId = e.OriginCronTickerId,
        DeleteBehavior = e.DeleteBehavior, RunCondition = e.RunCondition, ExecutedAt = e.ExecutedAt,
        LockHolder = e.LockHolder, LockedAt = e.LockedAt, OnStale = e.OnStale,
        TimeoutSeconds = e.TimeoutSeconds, AcquisitionToken = e.AcquisitionToken,
        ChainGeneration = e.ChainGeneration,
    };

    private static readonly Expression<Func<TCronTicker, TCronTicker>> CronMetadata = e => new TCronTicker
    {
        Id = e.Id, Function = e.Function, Description = e.Description, Expression = e.Expression,
        CreatedAt = e.CreatedAt, Retries = e.Retries, RetryIntervals = e.RetryIntervals,
        Chain = e.Chain, SkipIfAlreadyRunning = e.SkipIfAlreadyRunning,
        IsEnabled = e.IsEnabled, IsSystemPaused = e.IsSystemPaused,
        OnStale = e.OnStale, TimeoutSeconds = e.TimeoutSeconds,
    };

    private static readonly Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, CronTickerOccurrenceEntity<TCronTicker>>>
        OccurrenceMetadata = e => new CronTickerOccurrenceEntity<TCronTicker>
        {
            Id = e.Id, CronTickerId = e.CronTickerId, ChainRootId = e.ChainRootId, Status = e.Status,
            ExecutionTime = e.ExecutionTime, ElapsedTime = e.ElapsedTime, RetryCount = e.RetryCount,
            ExceptionMessage = e.ExceptionMessage, SkippedReason = e.SkippedReason, ExecutedAt = e.ExecutedAt,
            CreatedAt = e.CreatedAt, LockHolder = e.LockHolder, LockedAt = e.LockedAt,
            AcquisitionToken = e.AcquisitionToken,
        };

    private ITickerQueryable<TTimeTicker> DashboardTimeSource()
        => _persistence is ITickerDashboardQueryProvider<TTimeTicker> metadata
            ? metadata.TimeTickersDashboardQuery() : _persistence.TimeTickersQuery();

    private ITickerQueryable<TTimeTicker> TimeMetadataQuery()
        => DashboardMetadataQueryable<TTimeTicker>.Create(DashboardTimeSource(), TimeMetadata);

    private ITickerQueryable<TTimeTicker> ChainMetadataQuery()
    {
        var source = DashboardTimeSource();
        if (_persistence is ITickerDashboardQueryProvider<TTimeTicker>)
            return DashboardMetadataQueryable<TTimeTicker>.Create(source, TimeMetadata);
        if (source is not ITickerProjectionQueryable<TTimeTicker> { SupportsProjection: true }) return source;
        // Redis stores descendants inside root aggregates and exposes flat rows through
        // its monitoring query. Relational and document providers use the default bridge.
        return DashboardMetadataQueryable<TTimeTicker>.Create(
            _persistence.TimeTickersMonitoringQuery() ?? source, TimeMetadata);
    }

    private ITickerQueryable<TCronTicker> CronMetadataQuery()
        => DashboardMetadataQueryable<TCronTicker>.Create(_persistence.CronTickersQuery(), CronMetadata);

    private Task<TCronTicker> ReadCronMetadataAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = _persistence.CronTickersQuery();
        return source is ITickerProjectionQueryable<TCronTicker> { SupportsProjection: true }
            ? DashboardMetadataQueryable<TCronTicker>.Create(source, CronMetadata)
                .Where(x => x.Id == id).FirstOrDefaultAsync(cancellationToken)
            : _persistence.GetCronTickerById(id, cancellationToken);
    }

    private ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> OccurrenceMetadataQuery()
        => DashboardMetadataQueryable<CronTickerOccurrenceEntity<TCronTicker>>.Create(
            _persistence.CronTickerOccurrencesQuery(), OccurrenceMetadata);
}
