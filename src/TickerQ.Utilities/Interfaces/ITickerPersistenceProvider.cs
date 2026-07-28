using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
    {
        #region Time_Ticker_Core_Methods
        IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, CancellationToken cancellationToken = default);
        IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default);
        Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default);
        Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default);
        Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken);
        Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        /// <summary>
        /// Atomically transitions only queued rows still owned by this provider instance under
        /// the supplied acquisition generation. Returns exactly the ids that won the CAS.
        /// The fail-closed default prevents external providers from dispatching unfenced work.
        /// </summary>
        Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<Guid>());
        Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default);
        /// <summary>
        /// Atomically revives and acquires one on-demand time ticker. Implementations must transition
        /// only Idle/Queued/terminal rows (never InProgress), clear prior terminal/ownership metadata,
        /// stamp a fresh ownership generation, and return null when another caller wins.
        /// </summary>
        Task<TimeTickerEntity> AcquireTimeTickerOnDemandAsync(Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
            => Task.FromResult<TimeTickerEntity>(null);
        #endregion
        
        #region Cron_Ticker_Core_Methods
        /// <summary>
        /// Legacy seeding contract retained for source/binary compatibility with third-party providers.
        /// Built-in providers override the richer overload below; legacy providers continue to receive
        /// the function/expression projection through this default-interface bridge.
        /// </summary>
        Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
            => MigrateDefinedCronTickers(
                Array.ConvertAll(cronTickers, static ticker =>
                    new DefinedCronTickerSeed(ticker.Function, ticker.Expression)),
                cancellationToken);

        /// <summary>
        /// Seeds code-defined cron tickers with their request-contract identity. The default adapter
        /// projects to the legacy tuple overload so providers compiled against the earlier interface
        /// remain usable; built-in and identity-aware providers override this overload directly.
        /// </summary>
        Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
            => MigrateDefinedCronTickers(
                Array.ConvertAll(cronTickers, static ticker => (ticker.Function, ticker.Expression)),
                cancellationToken);
        Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken);
        Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default);
        #endregion
        
        #region Cron_TickerOccurrence_Core_Methods
        Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default);
        IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, CancellationToken cancellationToken = default);
        IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(CancellationToken cancellationToken = default);
        Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default);
        Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<Guid>());
        Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default);
        Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default) => Task.FromResult(0);
        #endregion

        #region Stale_Job_Recovery
        /// <summary>
        /// Whether this provider fully implements lease renewal and stale-job recovery.
        /// Defaults to <c>false</c> so a provider (or a partial third-party override) that
        /// has not implemented the whole contract fails closed: the background renewal and
        /// watchdog loops skip it instead of trusting the compatibility-safe defaults below.
        /// EF Core and MongoDB override this to <c>true</c>.
        /// </summary>
        bool SupportsLeaseBasedRecovery => false;
        // Default implementations keep external providers (e.g. Redis) compiling; they opt in
        // by overriding both these members and SupportsLeaseBasedRecovery. The defaults claim
        // NO success — zero renewed, nothing still held, nothing recovered — so a provider that
        // never renews a lease cannot masquerade as one that does.
        Task<int> RenewTimeTickerLeases(Guid[] timeTickerIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
        Task<int> RenewCronTickerOccurrenceLeases(Guid[] occurrenceIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
        /// <summary>Of the given ids, returns those still held (InProgress + locked) by this node.</summary>
        Task<Guid[]> GetStillHeldTickerIds(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<Guid>());

        // Generation-aware (fenced) lease operations. These are the safe overloads: they match
        // on id + AcquisitionToken so a row re-acquired under a newer generation (same node ABA)
        // or recovered by another node is never renewed/confirmed under a stale owner's snapshot.
        //
        // The default implementations FAIL CLOSED — they claim NOTHING renewed and NOTHING held,
        // and deliberately do NOT delegate to the ID-only overloads above. A provider that
        // advertises SupportsLeaseBasedRecovery but has not implemented generation fencing must
        // not silently fall back to unfenced ID-only renewal; failing closed surfaces its running
        // jobs as lost (local execution cancelled) instead of masquerading as safe. EF Core and
        // MongoDB override all three with per-id+token predicates.
        Task<int> RenewTimeTickerLeases(IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
        Task<int> RenewCronTickerOccurrenceLeases(IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
        /// <summary>
        /// Of the given leases, returns the ids still held by this node under the SAME generation
        /// (InProgress + locked by this node + matching AcquisitionToken). Fail-closed default.
        /// </summary>
        Task<Guid[]> GetStillHeldTickerIds(IReadOnlyCollection<AcquisitionLease> timeTickerLeases, IReadOnlyCollection<AcquisitionLease> occurrenceLeases, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<Guid>());
        /// <summary>
        /// Applies the OnStale policy to InProgress tickers whose lease expired:
        /// Restart (bounded by <paramref name="maxStaleRestarts"/>) resets them to
        /// Idle for re-acquisition; Cancel (or exhausted restarts) marks them
        /// Cancelled with a stale reason.
        /// </summary>
        Task<StaleTickerRecoveryResult> RecoverStaleTickers(int maxStaleRestarts, CancellationToken cancellationToken = default)
            => Task.FromResult(new StaleTickerRecoveryResult());
        #endregion
        
        #region Retention
        /// <summary>
        /// Whether this provider implements the built-in job-retention delete methods below with
        /// semantically-correct, bounded, concurrency-safe behavior. Defaults to <c>false</c> so a
        /// third-party provider that has not implemented retention fails closed: if retention is
        /// explicitly configured against it, startup surfaces a clear error instead of silently
        /// deleting nothing. Built-in providers (EF Core, MongoDB, Redis, in-memory) override to <c>true</c>.
        /// </summary>
        bool SupportsRetention => false;

        /// <summary>
        /// Repairs provider-specific retention indexes in one bounded, resumable step. Providers
        /// without secondary retention indexes inherit the completed no-op for source compatibility.
        /// </summary>
        Task<RetentionIndexReconciliationResult> ReconcileRetentionIndexesAsync(
            int batchSize, CancellationToken cancellationToken = default)
            => Task.FromResult(RetentionIndexReconciliationResult.Completed);

        /// <summary>
        /// Examines up to <paramref name="batchSize"/> candidate root chains starting strictly after
        /// <paramref name="cursor"/> (keyset ordering by <c>(ExecutedAt, Id)</c> ascending), deletes those
        /// that are fully eligible, and returns the rows removed, whether more candidates remain, and the
        /// keyset position to resume from.
        /// <para>
        /// A chain is deleted only when EVERY node in its arbitrary-depth subtree is individually
        /// eligible: terminal status, <c>ExecutedAt</c> non-null and strictly older than the cutoff for
        /// that node's status (<see cref="RetentionCutoffs.ForStatus"/>), and not actively owned (no live
        /// <c>AcquisitionToken</c> generation and no still-live <c>LeaseUntil</c>; a stale <c>LockHolder</c>
        /// left on a terminal row is NOT an active claim). If any node is ineligible — including a status
        /// whose window is null — the WHOLE chain is retained. Deletion is all-or-nothing per chain and must
        /// re-check eligibility at delete time so a concurrently reactivated chain is never partially erased.
        /// </para>
        /// <para>
        /// The cursor advances past examined-but-retained (blocked) chains so they cannot starve later
        /// eligible chains; when traversal reaches the end the returned
        /// <see cref="RetentionChainBatchResult.NextCursor"/> is <see cref="RetentionCursor.Start"/> so a
        /// later sweep wraps and reconsiders chains that became eligible behind the previous position. Cron
        /// ticker definitions are never touched.
        /// </para>
        /// The default FAILS CLOSED: providers that do not support retention must not silently no-op.
        /// </summary>
        Task<RetentionChainBatchResult> DeleteEligibleTimeTickerChainsAsync(
            RetentionCutoffs cutoffs, int batchSize, RetentionCursor cursor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "This persistence provider does not support built-in job retention. " +
                "Remove ConfigureJobRetention or use a provider that implements SupportsRetention.");

        /// <summary>
        /// Deletes up to <paramref name="batchSize"/> eligible historical cron-ticker occurrence rows and
        /// returns the number removed plus whether more remain. Eligible = terminal status, <c>ExecutedAt</c>
        /// non-null and older than the matching cutoff, and not active/locked/owned/leased. Cron ticker
        /// <b>definitions</b> are never deleted. Eligibility is re-checked at delete time. Fails closed by default.
        /// </summary>
        Task<RetentionBatchResult> DeleteEligibleCronTickerOccurrencesAsync(
            RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "This persistence provider does not support built-in job retention. " +
                "Remove ConfigureJobRetention or use a provider that implements SupportsRetention.");
        #endregion

        #region Queryable
        ITickerQueryable<TTimeTicker> TimeTickersQuery();
        ITickerQueryable<TCronTicker> CronTickersQuery();
        ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery();
        #endregion

        #region Time_Ticker_Shared_Methods
        Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default);
        Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default);
        Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default);
        Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default);
        /// <summary>
        /// Atomically replaces the entire time-ticker chain aggregate rooted at
        /// <paramref name="oldRootId"/> with <paramref name="newRoot"/> (root plus its whole
        /// descendant tree). Implementations MUST persist the complete replacement first and
        /// only then remove the original, all within a single transaction, so any validation
        /// or persistence failure rolls back fully and leaves the original aggregate — every
        /// node and every field (requests, retry intervals, stale/timeout/conditions) —
        /// untouched. Returns the number of new nodes inserted.
        ///
        /// The default FAILS CLOSED: a provider that has not implemented a genuinely atomic
        /// replace must not silently fall back to a non-atomic delete-then-create (the very
        /// data-loss window this method exists to close). EF Core and the in-memory provider
        /// override it; Mongo/Redis (no transactional replace yet) surface a clear error.
        /// </summary>
        Task<int> ReplaceTimeTickerChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "This persistence provider does not support atomic time-ticker chain replacement. " +
                "Editing a chain would require a non-atomic delete-then-create that can lose the original on failure.");
        #endregion

        #region Cron_Ticker_Shared_Methods
        Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken);
        Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken);
        Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken);
        Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken);
        Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken);
        #endregion
        
        #region Cron_TickerOccurrence_Shared_Methods
        Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default);
        Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken);
        Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken);
        Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        #endregion
    }
}
