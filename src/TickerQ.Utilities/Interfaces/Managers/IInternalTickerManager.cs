using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    internal interface IInternalTickerManager
    {
        Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(CancellationToken cancellationToken = default);
        Task ReleaseAcquiredResources(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
        Task<InternalFunctionContext[]> SetTickersInProgress(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
        Task UpdateTickerAsync(InternalFunctionContext context, CancellationToken cancellationToken = default);
        [RequiresUnreferencedCode("Legacy request deserialization may use reflection metadata. Use the JsonTypeInfo overload for trimming/AOT.")]
        [RequiresDynamicCode("Legacy request deserialization may require runtime JSON metadata. Use the JsonTypeInfo overload for Native AOT.")]
        Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default);
        Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default);
        Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default);
        Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default);
        Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default);
        Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default);
        Task UpdateSkipTimeTickersWithUnifiedContextAsync(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
        Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default);
        /// <summary>Whether the configured persistence provider supports lease renewal and stale-job recovery.</summary>
        bool SupportsLeaseBasedRecovery => false;
        /// <summary>Renews leases for this node's running tickers; returns the renewed count.</summary>
        Task<int> RenewActiveTickerLeasesAsync(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        /// <summary>Of the given running ids, returns those this node no longer holds (lease was lost).</summary>
        Task<Guid[]> GetLostLeaseTickerIdsAsync(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        /// <summary>
        /// Generation-aware lease renewal: renews only rows still held under the exact
        /// <see cref="AcquisitionLease.AcquisitionToken"/> generation; returns the renewed count.
        /// </summary>
        Task<int> RenewActiveTickerLeasesAsync(IReadOnlyCollection<AcquisitionLease> timeTickerLeases, IReadOnlyCollection<AcquisitionLease> occurrenceLeases, CancellationToken cancellationToken = default);
        /// <summary>
        /// Generation-aware lost-lease detection preserving the persistence namespace for each row.
        /// </summary>
        Task<TickerExecutionLease[]> GetLostLeaseTickerIdsAsync(IReadOnlyCollection<AcquisitionLease> timeTickerLeases, IReadOnlyCollection<AcquisitionLease> occurrenceLeases, CancellationToken cancellationToken = default);
        /// <summary>One watchdog sweep: applies OnStale to expired-lease InProgress tickers.</summary>
        Task<StaleTickerRecoveryResult> RecoverStaleTickersAsync(CancellationToken cancellationToken = default);
    }
}
