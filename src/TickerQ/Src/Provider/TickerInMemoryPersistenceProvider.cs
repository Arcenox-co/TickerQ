using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Provider
{
    internal class
        TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPersistenceProvider<TTimeTicker,
        TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private sealed class PartitionState
        {
            internal readonly ConcurrentDictionary<Guid, TTimeTicker> TimeTickers = new();
            internal readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> ChildrenIndex = new();
            internal readonly ConcurrentDictionary<Guid, TCronTicker> CronTickers = new();
            internal readonly ConcurrentDictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>> CronOccurrences = new();
            internal readonly ConcurrentDictionary<(DateTime ExecutionTime, Guid CronTickerId), Guid> CronOccurrenceIndex = new();
            internal readonly ConcurrentDictionary<Guid, TickerResultEnvelope> TimeTickerResults = new();
            internal readonly ConcurrentDictionary<Guid, TickerResultEnvelope> CronOccurrenceResults = new();
            internal readonly Dictionary<string, ActivationEpochState> ScopedActivationEpochs = new(StringComparer.Ordinal);
            internal readonly ReaderWriterLockSlim GraphLock = new(LockRecursionPolicy.NoRecursion);
            internal readonly SemaphoreSlim AcquisitionPublicationGate = new(1, 1);
            internal ActivationEpochState ActivationEpoch = ActivationEpochState.PreEpoch;
        }

        private static readonly ConcurrentDictionary<string, PartitionState> Partitions =
            new(StringComparer.Ordinal);
        private static readonly object LegacyAdoptionGate = new();
        private static string _legacyAdoptionOwner;
        private static long _legacyAdoptionEpoch;
        private static bool _legacyAdoptionCompleted;
        private readonly PartitionState _partitionState;
        private readonly string _runtimePartitionKey;
        private ConcurrentDictionary<Guid, TTimeTicker> TimeTickers => _partitionState.TimeTickers;
        private ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> ChildrenIndex => _partitionState.ChildrenIndex;
        private ConcurrentDictionary<Guid, TCronTicker> CronTickers => _partitionState.CronTickers;
        private ConcurrentDictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>> CronOccurrences => _partitionState.CronOccurrences;
        private ConcurrentDictionary<(DateTime ExecutionTime, Guid CronTickerId), Guid> CronOccurrenceIndex => _partitionState.CronOccurrenceIndex;
        private ConcurrentDictionary<Guid, TickerResultEnvelope> TimeTickerResults => _partitionState.TimeTickerResults;
        private ConcurrentDictionary<Guid, TickerResultEnvelope> CronOccurrenceResults => _partitionState.CronOccurrenceResults;
        private Dictionary<string, ActivationEpochState> ScopedActivationEpochs => _partitionState.ScopedActivationEpochs;
        private ReaderWriterLockSlim GraphLock => _partitionState.GraphLock;
        private SemaphoreSlim AcquisitionPublicationGate => _partitionState.AcquisitionPublicationGate;
        private ActivationEpochState _activationEpoch
        {
            get => _partitionState.ActivationEpoch;
            set => _partitionState.ActivationEpoch = value;
        }
        private ActivationEpochState DefaultActivationEpoch
        {
            get => _runtimeScopeBindingConfigured && _runtimeActivationScopeKey != null
                ? ScopedActivationEpochs.TryGetValue(_runtimeActivationScopeKey, out var state)
                    ? state
                    : ActivationEpochState.PreEpoch
                : _activationEpoch;
            set
            {
                if (_runtimeScopeBindingConfigured && _runtimeActivationScopeKey != null)
                    ScopedActivationEpochs[_runtimeActivationScopeKey] = value;
                else
                    _activationEpoch = value;
            }
        }
        internal ConcurrentDictionary<Guid, TTimeTicker> TimeTickersForTests => TimeTickers;
        internal ConcurrentDictionary<Guid, TCronTicker> CronTickersForTests => CronTickers;
        internal ConcurrentDictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>> CronOccurrencesForTests => CronOccurrences;
        internal ConcurrentDictionary<Guid, TickerResultEnvelope> CronOccurrenceResultsForTests => CronOccurrenceResults;
        internal static Action AfterLegacyAdoptionFirstCollectionCopiedForTest { get; set; }

        /// <summary>Test-only reset of the process-wide activation epoch latch.</summary>
        internal static void ResetActivationEpochForTests()
        {
            lock (LegacyAdoptionGate)
            {
                _legacyAdoptionOwner = null;
                _legacyAdoptionEpoch = 0;
                _legacyAdoptionCompleted = false;
                AfterLegacyAdoptionFirstCollectionCopiedForTest = null;
                foreach (var state in Partitions.Values)
                {
                    state.GraphLock.EnterWriteLock();
                    try
                    {
                        state.ActivationEpoch = ActivationEpochState.PreEpoch;
                        state.ScopedActivationEpochs.Clear();
                    }
                    finally { state.GraphLock.ExitWriteLock(); }
                }
            }
        }

        internal static void ResetAllStateForTests()
        {
            lock (LegacyAdoptionGate)
            {
                _legacyAdoptionOwner = null;
                _legacyAdoptionEpoch = 0;
                _legacyAdoptionCompleted = false;
                AfterLegacyAdoptionFirstCollectionCopiedForTest = null;
                AfterCronOccurrenceTerminalMutationForTest = null;
                AfterTimeTickerOnDemandMutationForTest = null;
                AfterAcquisitionMutationForTest = null;
                foreach (var state in Partitions.Values)
                {
                    state.GraphLock.EnterWriteLock();
                    try
                    {
                        state.TimeTickers.Clear();
                        state.ChildrenIndex.Clear();
                        state.CronTickers.Clear();
                        state.CronOccurrences.Clear();
                        state.CronOccurrenceIndex.Clear();
                        state.TimeTickerResults.Clear();
                        state.CronOccurrenceResults.Clear();
                        state.ActivationEpoch = ActivationEpochState.PreEpoch;
                        state.ScopedActivationEpochs.Clear();
                    }
                    finally { state.GraphLock.ExitWriteLock(); }
                }
            }
        }

        private readonly ITickerClock _clock;
        private readonly string _lockHolder;
        private readonly TimeSpan _leaseDuration;
        private readonly TimeSpan _retirementGracePeriod;
        private readonly long _reconciliationEpoch;
        private readonly string _runtimeActivationScopeKey;
        private readonly bool _runtimeScopeBindingConfigured;
        private readonly bool _runtimeSchedulerEnabled;

        public TickerInMemoryPersistenceProvider(IServiceProvider serviceProvider)
        {
            _clock = serviceProvider.GetService<ITickerClock>() ?? new TickerSystemClock();
            var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
            _lockHolder = optionsBuilder?.ExecutionOwnerId ?? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
            _leaseDuration = optionsBuilder?.LeaseDuration ?? TimeSpan.FromMinutes(1);
            _retirementGracePeriod = optionsBuilder?.DefinedCronRetirementGracePeriod ?? TimeSpan.FromHours(24);
            _reconciliationEpoch = optionsBuilder?.ReconciliationEpoch ?? 1;
            _runtimeScopeBindingConfigured = optionsBuilder?.HasRuntimeActivationScopeBinding ?? false;
            _runtimeSchedulerEnabled = optionsBuilder?.RuntimeSchedulerEnabled ?? true;
            _runtimeActivationScopeKey = optionsBuilder?.RuntimeActivationScope?.ScopeKey;
            var partition = optionsBuilder?.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal;
            _runtimePartitionKey = partition.StorageKey;
            _partitionState = Partitions.GetOrAdd(partition.StorageKey, _ => new PartitionState());
        }

        // In-memory persistence has no lease/stale-recovery contract, so it opts out
        // explicitly and inherits the compatibility-safe (fail-closed) interface defaults.
        public bool SupportsLeaseBasedRecovery => false;
        public bool SupportsLegacyRuntimePartitionAdoption => true;

        public Task AdoptLegacyRuntimePartitionAsync(
            LegacyRuntimePartitionAdoption adoption, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(adoption);
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(adoption.TargetPartition.StorageKey, _runtimePartitionKey))
                throw new InvalidOperationException("Legacy adoption target does not match this provider's runtime partition.");

            lock (LegacyAdoptionGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_legacyAdoptionOwner != null &&
                    (!StringComparer.Ordinal.Equals(_legacyAdoptionOwner, _runtimePartitionKey) ||
                     _legacyAdoptionEpoch != adoption.Epoch))
                    throw new InvalidOperationException(
                        "Legacy runtime state is already held or completed by a different adoption owner or epoch.");
                if (_legacyAdoptionCompleted)
                    return Task.CompletedTask;

                var legacy = Partitions.GetOrAdd(
                    TickerQRuntimePartition.LegacyGlobal.StorageKey, _ => new PartitionState());
                if (ReferenceEquals(legacy, _partitionState))
                    throw new InvalidOperationException("Legacy runtime state cannot be adopted into the legacy partition.");

                legacy.AcquisitionPublicationGate.Wait(cancellationToken);
                try
                {
                    _partitionState.AcquisitionPublicationGate.Wait(cancellationToken);
                    try
                    {
                        legacy.GraphLock.EnterWriteLock();
                        try
                        {
                            // Publish the process tombstone under the legacy graph lock before
                            // scanning or copying. Interrupted attempts stay fenced; only this
                            // owner/epoch can resume.
                            _legacyAdoptionOwner = _runtimePartitionKey;
                            _legacyAdoptionEpoch = adoption.Epoch;
                            _partitionState.GraphLock.EnterWriteLock();
                            try
                            {
                                var foreignOwners = Partitions.Where(pair =>
                                        pair.Key != TickerQRuntimePartition.LegacyGlobal.StorageKey &&
                                        pair.Key != _runtimePartitionKey && HasRuntimeState(pair.Value))
                                    .Select(pair => pair.Key).ToArray();
                                if (foreignOwners.Length > 0)
                                    throw new InvalidOperationException(
                                        "Legacy runtime adoption is ambiguous because runtime rows already exist for another namespace.");

                                PreflightLegacyState(legacy, _partitionState);
                                CopyLegacyState(legacy, _partitionState, _runtimePartitionKey);
                                _legacyAdoptionCompleted = true;
                            }
                            finally
                            {
                                _partitionState.GraphLock.ExitWriteLock();
                            }
                        }
                        finally
                        {
                            legacy.GraphLock.ExitWriteLock();
                        }
                    }
                    finally
                    {
                        _partitionState.AcquisitionPublicationGate.Release();
                    }
                }
                finally
                {
                    legacy.AcquisitionPublicationGate.Release();
                }
                return Task.CompletedTask;
            }
        }

        private static bool HasRuntimeState(PartitionState state)
            => !state.TimeTickers.IsEmpty || !state.CronTickers.IsEmpty ||
               !state.CronOccurrences.IsEmpty || !state.TimeTickerResults.IsEmpty ||
               !state.CronOccurrenceResults.IsEmpty;

        private static void PreflightLegacyState(PartitionState source, PartitionState target)
        {
            if (source.TimeTickers.Keys.Any(target.TimeTickers.ContainsKey))
                throw new InvalidOperationException("The target partition already contains a conflicting TimeTicker ID.");
            if (source.CronTickers.Keys.Any(target.CronTickers.ContainsKey))
                throw new InvalidOperationException("The target partition already contains a conflicting CronTicker ID.");
            if (source.CronOccurrences.Keys.Any(target.CronOccurrences.ContainsKey))
                throw new InvalidOperationException("The target partition already contains a conflicting Cron occurrence ID.");
            if (source.ChildrenIndex.Keys.Any(target.ChildrenIndex.ContainsKey))
                throw new InvalidOperationException("The target partition already contains a conflicting child index ID.");
            if (source.CronOccurrenceIndex.Keys.Any(target.CronOccurrenceIndex.ContainsKey))
                throw new InvalidOperationException("The target partition already contains a conflicting Cron occurrence index.");
            if (source.TimeTickerResults.Keys.Any(target.TimeTickerResults.ContainsKey))
                throw new InvalidOperationException("The target partition already contains a conflicting TimeTicker result ID.");
            if (source.CronOccurrenceResults.Keys.Any(target.CronOccurrenceResults.ContainsKey))
                throw new InvalidOperationException("The target partition already contains a conflicting Cron occurrence result ID.");
        }

        private static void CopyLegacyState(PartitionState source, PartitionState target, string targetKey)
        {
            var stampedTimeTickers = source.TimeTickers.Values
                .SelectMany(FlattenTimeTickerGraph)
                .Distinct()
                .Select(entity => (Entity: entity, Partition: entity.ApplicationNamespaceKey))
                .ToArray();
            var stampedCronTickers = source.CronTickers.Values
                .Select(entity => (Entity: entity, Partition: entity.ApplicationNamespaceKey))
                .ToArray();
            var stampedOccurrences = source.CronOccurrences.Values
                .Select(entity => (Entity: entity, Partition: entity.ApplicationNamespaceKey))
                .ToArray();
            var addedTimeTickers = new List<Guid>();
            var addedCronTickers = new List<Guid>();
            var addedOccurrences = new List<Guid>();
            var addedChildrenIndexes = new List<Guid>();
            var addedOccurrenceIndexes = new List<(DateTime ExecutionTime, Guid CronTickerId)>();
            var addedTimeResults = new List<Guid>();
            var addedOccurrenceResults = new List<Guid>();

            try
            {
                foreach (var pair in source.TimeTickers)
                {
                    StampTimeTickerPartition(pair.Value, targetKey);
                    if (!target.TimeTickers.TryAdd(pair.Key, pair.Value))
                        throw new InvalidOperationException("The target partition already contains a conflicting TimeTicker ID.");
                    addedTimeTickers.Add(pair.Key);
                }
                AfterLegacyAdoptionFirstCollectionCopiedForTest?.Invoke();
                foreach (var pair in source.CronTickers)
                {
                    pair.Value.ApplicationNamespaceKey = targetKey;
                    if (!target.CronTickers.TryAdd(pair.Key, pair.Value))
                        throw new InvalidOperationException("The target partition already contains a conflicting CronTicker ID.");
                    addedCronTickers.Add(pair.Key);
                }
                foreach (var pair in source.CronOccurrences)
                {
                    pair.Value.ApplicationNamespaceKey = targetKey;
                    if (!target.CronOccurrences.TryAdd(pair.Key, pair.Value))
                        throw new InvalidOperationException("The target partition already contains a conflicting Cron occurrence ID.");
                    addedOccurrences.Add(pair.Key);
                }
                CopyIndex(source.ChildrenIndex, target.ChildrenIndex, addedChildrenIndexes);
                CopyIndex(source.CronOccurrenceIndex, target.CronOccurrenceIndex, addedOccurrenceIndexes);
                CopyIndex(source.TimeTickerResults, target.TimeTickerResults, addedTimeResults);
                CopyIndex(source.CronOccurrenceResults, target.CronOccurrenceResults, addedOccurrenceResults);

                source.TimeTickers.Clear(); source.CronTickers.Clear(); source.CronOccurrences.Clear();
                source.ChildrenIndex.Clear(); source.CronOccurrenceIndex.Clear();
                source.TimeTickerResults.Clear(); source.CronOccurrenceResults.Clear();
            }
            catch
            {
                RemoveKeys(target.TimeTickers, addedTimeTickers);
                RemoveKeys(target.CronTickers, addedCronTickers);
                RemoveKeys(target.CronOccurrences, addedOccurrences);
                RemoveKeys(target.ChildrenIndex, addedChildrenIndexes);
                RemoveKeys(target.CronOccurrenceIndex, addedOccurrenceIndexes);
                RemoveKeys(target.TimeTickerResults, addedTimeResults);
                RemoveKeys(target.CronOccurrenceResults, addedOccurrenceResults);
                foreach (var stamped in stampedTimeTickers) stamped.Entity.ApplicationNamespaceKey = stamped.Partition;
                foreach (var stamped in stampedCronTickers) stamped.Entity.ApplicationNamespaceKey = stamped.Partition;
                foreach (var stamped in stampedOccurrences) stamped.Entity.ApplicationNamespaceKey = stamped.Partition;
                throw;
            }
        }

        private static IEnumerable<TTimeTicker> FlattenTimeTickerGraph(TTimeTicker root)
        {
            yield return root;
            if (root.Children == null) yield break;
            foreach (var child in root.Children)
            foreach (var descendant in FlattenTimeTickerGraph(child))
                yield return descendant;
        }

        private static void CopyIndex<TKey, TValue>(
            ConcurrentDictionary<TKey, TValue> source,
            ConcurrentDictionary<TKey, TValue> target,
            ICollection<TKey> added)
        {
            foreach (var pair in source)
            {
                if (!target.TryAdd(pair.Key, pair.Value))
                    throw new InvalidOperationException("The target partition changed after legacy-adoption preflight.");
                added.Add(pair.Key);
            }
        }

        private static void RemoveKeys<TKey, TValue>(
            ConcurrentDictionary<TKey, TValue> target, IEnumerable<TKey> keys)
        {
            foreach (var key in keys) target.TryRemove(key, out _);
        }

        private static void StampTimeTickerPartition(TTimeTicker ticker, string targetKey)
        {
            ticker.ApplicationNamespaceKey = targetKey;
            if (ticker.Children == null) return;
            foreach (var child in ticker.Children) StampTimeTickerPartition(child, targetKey);
        }

        #region Reconciliation_Activation_Epoch
        public bool SupportsReconciliationActivationEpoch => true;
        public bool SupportsAuthoritativeCronReconciliation => true;

        public Task<ActivationEpochState> GetReconciliationActivationStateAsync(
            ReconciliationActivationScope scope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReadGraph(() => ScopedActivationEpochs.TryGetValue(scope.ScopeKey, out var state)
                ? state : ActivationEpochState.PreEpoch));
        }

        public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(
            ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                var state = ScopedActivationEpochs.TryGetValue(scope.ScopeKey, out var current)
                    ? current : ActivationEpochState.PreEpoch;
                if (state.Epoch > targetEpoch ||
                    (state.Epoch == targetEpoch && state.Phase is ActivationEpochPhase.Activating or ActivationEpochPhase.Activated))
                    return state;
                state = new ActivationEpochState { Epoch = targetEpoch, Phase = ActivationEpochPhase.Activating };
                ScopedActivationEpochs[scope.ScopeKey] = state;
                return state;
            }));
        }

        public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(
            ReconciliationActivationScope scope, long targetEpoch, string checkpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                var state = ScopedActivationEpochs.TryGetValue(scope.ScopeKey, out var current)
                    ? current : ActivationEpochState.PreEpoch;
                if (state.Epoch == targetEpoch && state.Phase == ActivationEpochPhase.Activating)
                {
                    state = new ActivationEpochState
                    {
                        Epoch = targetEpoch, Phase = ActivationEpochPhase.Activating, Checkpoint = checkpoint
                    };
                    ScopedActivationEpochs[scope.ScopeKey] = state;
                }
                return state;
            }));
        }

        public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(
            ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                var state = ScopedActivationEpochs.TryGetValue(scope.ScopeKey, out var current)
                    ? current : ActivationEpochState.PreEpoch;
                if (state.Phase == ActivationEpochPhase.Activated && state.Epoch >= targetEpoch)
                    return state;
                if (state.Epoch != targetEpoch || state.Phase != ActivationEpochPhase.Activating)
                    return state;
                state = new ActivationEpochState
                {
                    Epoch = targetEpoch, Phase = ActivationEpochPhase.Activated, Checkpoint = state.Checkpoint
                };
                ScopedActivationEpochs[scope.ScopeKey] = state;
                return state;
            }));
        }

        public Task<ActivationEpochState> GetReconciliationActivationStateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReadGraph(() => DefaultActivationEpoch));
        }

        public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(long targetEpoch, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                var state = DefaultActivationEpoch;
                if (state.Epoch > targetEpoch ||
                    (state.Epoch == targetEpoch && state.Phase is ActivationEpochPhase.Activating or ActivationEpochPhase.Activated))
                    return state;

                DefaultActivationEpoch = new ActivationEpochState
                {
                    Epoch = targetEpoch,
                    Phase = ActivationEpochPhase.Activating,
                    Checkpoint = null
                };
                return DefaultActivationEpoch;
            }));
        }

        public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(long targetEpoch, string checkpoint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                var state = DefaultActivationEpoch;
                if (state.Epoch == targetEpoch && state.Phase == ActivationEpochPhase.Activating)
                    DefaultActivationEpoch = new ActivationEpochState
                    {
                        Epoch = targetEpoch,
                        Phase = ActivationEpochPhase.Activating,
                        Checkpoint = checkpoint
                    };
                return DefaultActivationEpoch;
            }));
        }

        public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(long targetEpoch, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                var state = DefaultActivationEpoch;
                if (state.Phase == ActivationEpochPhase.Activated && state.Epoch >= targetEpoch)
                    return state;
                if (state.Epoch != targetEpoch || state.Phase != ActivationEpochPhase.Activating)
                    return state;

                DefaultActivationEpoch = new ActivationEpochState
                {
                    Epoch = targetEpoch,
                    Phase = ActivationEpochPhase.Activated,
                    Checkpoint = state.Checkpoint
                };
                return DefaultActivationEpoch;
            }));
        }
        #endregion

        // Built-in provider: implements job retention with whole-chain, all-or-nothing semantics.
        public bool SupportsRetention => true;

        // Built-in provider: durably stores per-ticker result envelopes for parent-result propagation.
        public bool SupportsResultPublication => true;
        public bool SupportsAcknowledgedTerminalUpdates => true;
        public bool SupportsTimeTickerChainRepair => true;

        public Task<TimeTickerChainRepairResult> RepairTimeTickerChainsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState())
                    return TimeTickerChainRepairResult.Empty;
                DuringGlobalRepairMutationHook?.Invoke();
                var plan = TimeTickerChainRepairPlanner.Create(
                    TimeTickers.Values.ToArray(), cancellationToken);
                plan.ThrowIfMalformed();
                foreach (var update in plan.Updates)
                {
                    update.Row.ChainRootId = update.ChainRootId;
                    update.Row.ChainGeneration = update.ChainGeneration;
                    update.Row.UpdatedAt = _clock.UtcNow;
                }
                return plan.Result;
            }));
        }

        // A result becomes visible only on the final successful terminal write: a present result envelope
        // on a Done/DueDone terminal update. Failed/cancelled/skipped/retry writes never carry one (the
        // execution handler only attaches it on success), and this re-check guards against any that slip through.
        private static bool IsSuccessfulResultWrite(InternalFunctionContext functionContext)
            => functionContext.ResultEnvelope != null
               && functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))
               && functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status))
               && functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;

        public Task<TickerResultEnvelope> GetTimeTickerResultAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(ReadGraph(() =>
                TimeTickerResults.TryGetValue(id, out var envelope) ? envelope : null));

        public Task<TickerResultEnvelope> GetCronTickerOccurrenceResultAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(ReadGraph(() =>
                CronOccurrenceResults.TryGetValue(id, out var envelope) ? envelope : null));

        public Task<bool> CommitSuccessfulTickerAsync(
            InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            ValidateTerminalCommit(functionContext, requireSuccessful: true);
            return CommitTerminalTickerCoreAsync(functionContext, enforceRemoteChildToken: false, cancellationToken);
        }

        public Task<bool> CommitTerminalTickerAsync(
            InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            ValidateTerminalCommit(functionContext, requireSuccessful: false);
            return CommitTerminalTickerCoreAsync(functionContext, enforceRemoteChildToken: true, cancellationToken);
        }

        private void ValidateTerminalCommit(InternalFunctionContext context, bool requireSuccessful)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            EnsureExactTerminalPartition(context);
            var successful = context.Status is TickerStatus.Done or TickerStatus.DueDone;
            if (!context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
                context.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                    or TickerStatus.Cancelled or TickerStatus.Skipped) ||
                (requireSuccessful && !successful) ||
                (successful && !context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))))
                throw new InvalidOperationException(
                    "Acknowledged persistence accepts only a terminal mutation; success requires an explicit optional result envelope.");
        }

        private void EnsureExactTerminalPartition(InternalFunctionContext context)
        {
            if (string.IsNullOrWhiteSpace(context.RuntimePartitionKey))
            {
                if (_runtimePartitionKey == TickerQRuntimePartition.LegacyGlobal.StorageKey)
                {
                    context.RuntimePartitionKey = _runtimePartitionKey;
                    return;
                }
                throw new InvalidOperationException("A terminal mutation requires an exact runtime partition identity.");
            }
            if (!StringComparer.Ordinal.Equals(context.RuntimePartitionKey, _runtimePartitionKey))
                throw new InvalidOperationException("Terminal mutation runtime partition mismatch.");
        }

        private Task<bool> CommitTerminalTickerCoreAsync(
            InternalFunctionContext functionContext, bool enforceRemoteChildToken,
            CancellationToken cancellationToken)
        {
            var successful = functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (functionContext.Type == TickerType.CronTickerOccurrence)
                {
                    if (!CronOccurrences.TryGetValue(functionContext.TickerId, out var occurrence) ||
                        !functionContext.AcquisitionToken.HasValue || occurrence.LockHolder != _lockHolder ||
                        occurrence.AcquisitionToken != functionContext.AcquisitionToken)
                        return false;

                    var updatedOccurrence = CloneCronOccurrence(occurrence);
                    ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);
                    if (!TryUpdateCronOccurrence(functionContext.TickerId, updatedOccurrence, occurrence,
                            requireRunnableActivation: false))
                        return false;
                    if (successful)
                        ReplaceCommittedResult(CronOccurrenceResults, functionContext);
                    return true;
                }

                if (!TimeTickers.TryGetValue(functionContext.TickerId, out var ticker) ||
                    !HasCurrentChainGeneration(functionContext, ticker))
                    return false;

                if (functionContext.ParentId == null)
                {
                    if (!functionContext.AcquisitionToken.HasValue || ticker.LockHolder != _lockHolder ||
                        ticker.AcquisitionToken != functionContext.AcquisitionToken)
                        return false;
                }
                else
                {
                    if (ticker.ParentId != functionContext.ParentId)
                        return false;
                    // A remote child dispatch carries the root acquisition token. Bind that signed
                    // identity to the durable aggregate generation without requiring children to
                    // persist a duplicate acquisition token of their own.
                    if (enforceRemoteChildToken &&
                        (!functionContext.AcquisitionToken.HasValue ||
                         functionContext.AcquisitionToken != functionContext.ChainGeneration))
                        return false;
                }

                var updatedTicker = CloneTicker(ticker);
                ApplyFunctionContextToTicker(updatedTicker, functionContext);
                if (!TryUpdateTimeTicker(functionContext.TickerId, updatedTicker, ticker,
                        requireRunnableActivation: false))
                    return false;
                if (successful)
                    ReplaceCommittedResult(TimeTickerResults, functionContext);
                return true;
            }));
        }

        private static void ReplaceCommittedResult(
            ConcurrentDictionary<Guid, TickerResultEnvelope> results,
            InternalFunctionContext functionContext)
        {
            if (functionContext.ResultEnvelope == null)
                results.TryRemove(functionContext.TickerId, out _);
            else
                results[functionContext.TickerId] = functionContext.ResultEnvelope;
        }

        #region Time Ticker Methods

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await AcquisitionPublicationGate.WaitAsync(cancellationToken);
            try
            {
                var now = _clock.UtcNow;
                foreach (var timeTicker in timeTickers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TimeTickers.TryGetValue(timeTicker.Id, out var existingTicker)
                        && existingTicker.UpdatedAt == timeTicker.UpdatedAt)
                    {
                        var updatedTicker = CloneTicker(existingTicker);
                        updatedTicker.LockHolder = _lockHolder;
                        updatedTicker.LockedAt = now;
                        updatedTicker.AcquisitionToken = Guid.NewGuid();
                        updatedTicker.ChainRootId = updatedTicker.Id;
                        updatedTicker.ChainGeneration = updatedTicker.AcquisitionToken;
                        updatedTicker.UpdatedAt = now;
                        updatedTicker.Status = TickerStatus.Queued;
                        
                        if (TryUpdateTimeTicker(timeTicker.Id, updatedTicker, existingTicker))
                        {
                            timeTicker.UpdatedAt = now;
                            timeTicker.LockHolder = _lockHolder;
                            timeTicker.LockedAt = now;
                            timeTicker.AcquisitionToken = updatedTicker.AcquisitionToken;
                            timeTicker.ChainRootId = updatedTicker.Id;
                            timeTicker.ChainGeneration = updatedTicker.ChainGeneration;
                            timeTicker.Status = TickerStatus.Queued;

                            AfterAcquisitionMutationForTest?.Invoke(timeTicker.Id);
                            yield return timeTicker;
                        }
                    }
                }
            }
            finally
            {
                AcquisitionPublicationGate.Release();
            }
        }

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await AcquisitionPublicationGate.WaitAsync(cancellationToken);
            try
            {
                var now = _clock.UtcNow;
                var fallbackThreshold = now.AddSeconds(-1);
                var timeTickersToUpdate = TimeTickers.Values
                    .Where(x => x.ExecutionTime != null)
                    .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                    .Where(x => x.ExecutionTime <= fallbackThreshold)
                    .ToArray();

                foreach (var ticker in timeTickersToUpdate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TimeTickers.TryGetValue(ticker.Id, out var existingTicker)
                        && existingTicker.UpdatedAt <= ticker.UpdatedAt)
                    {
                        var updatedTicker = CloneTicker(existingTicker);
                        updatedTicker.LockHolder = _lockHolder;
                        updatedTicker.LockedAt = now;
                        updatedTicker.AcquisitionToken = Guid.NewGuid();
                        updatedTicker.ChainRootId = updatedTicker.Id;
                        updatedTicker.ChainGeneration = updatedTicker.AcquisitionToken;
                        updatedTicker.UpdatedAt = now;
                        updatedTicker.Status = TickerStatus.InProgress;

                        if (TryUpdateTimeTicker(ticker.Id, updatedTicker, existingTicker))
                        {
                            AfterAcquisitionMutationForTest?.Invoke(ticker.Id);
                            yield return ForQueueTimeTickers(updatedTicker);
                        }
                    }
                }
            }
            finally
            {
                AcquisitionPublicationGate.Release();
            }
        }

        public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
        {
            WriteGraph(() =>
            {
                var now = _clock.UtcNow;
                var idsToRelease = timeTickerIds == null || timeTickerIds.Length == 0
                    ? TimeTickers.Keys.ToArray()
                    : timeTickerIds;
                foreach (var id in idsToRelease)
                {
                    if (TimeTickers.TryGetValue(id, out var ticker) && CanReleaseOwned(ticker))
                    {
                        var updatedTicker = CloneTicker(ticker);
                        updatedTicker.LockHolder = null;
                        updatedTicker.LockedAt = null;
                        updatedTicker.LeaseUntil = null;
                        updatedTicker.AcquisitionToken = null;
                        updatedTicker.ChainGeneration = null;
                        updatedTicker.Status = TickerStatus.Idle;
                        updatedTicker.UpdatedAt = now;

                        TryUpdateTimeTicker(id, updatedTicker, ticker);
                    }
                }
                return 0;
            });

            return Task.CompletedTask;
        }

        public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReadGraph(() =>
            {
                if (!IsRunnableActivationState()) return Array.Empty<TimeTickerEntity>();
                var oneSecondAgo = _clock.UtcNow.AddSeconds(-1);
                var baseQuery = TimeTickers.Values
                    .Where(x => x.ExecutionTime != null)
                    .Where(CanAcquire)
                    .Where(x => x.ExecutionTime >= oneSecondAgo)
                    .ToArray();
                var minExecutionTime = baseQuery.OrderBy(x => x.ExecutionTime)
                    .Select(x => x.ExecutionTime).FirstOrDefault();
                if (minExecutionTime == null) return Array.Empty<TimeTickerEntity>();
                var minSecond = new DateTime(
                    minExecutionTime.Value.Year, minExecutionTime.Value.Month, minExecutionTime.Value.Day,
                    minExecutionTime.Value.Hour, minExecutionTime.Value.Minute, minExecutionTime.Value.Second,
                    DateTimeKind.Utc);
                var maxExecutionTime = minSecond.AddSeconds(1);
                return baseQuery
                    .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
                    .OrderBy(x => x.ExecutionTime)
                    .Select(ForQueueTimeTickers)
                    .ToArray();
            }));
        }

        public Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                if (!TimeTickers.TryGetValue(functionContext.TickerId, out var ticker) ||
                    !HasCurrentChainGeneration(functionContext, ticker))
                    return 0;

                if (IsFencedTerminalWrite(functionContext) && functionContext.ParentId == null &&
                    (!functionContext.AcquisitionToken.HasValue || ticker.LockHolder != _lockHolder ||
                     ticker.AcquisitionToken != functionContext.AcquisitionToken))
                    return 0;

                var updatedTicker = CloneTicker(ticker);
                ApplyFunctionContextToTicker(updatedTicker, functionContext);

                EligibilityMutationLockHook?.Invoke(functionContext.TickerId);
                if (!TryUpdateTimeTicker(functionContext.TickerId, updatedTicker, ticker,
                        requireRunnableActivation: !IsFencedTerminalWrite(functionContext)))
                    return 0;

                if (IsSuccessfulResultWrite(functionContext))
                    TimeTickerResults[functionContext.TickerId] = functionContext.ResultEnvelope;
                return 1;
            }));
        }

        private bool HasCurrentChainGeneration(
            InternalFunctionContext context, TTimeTicker target)
        {
            if (context.ParentId == null)
                return !context.ChainGeneration.HasValue ||
                       (context.ChainRootId == target.Id &&
                        target.ChainRootId == target.Id &&
                        target.ChainGeneration == context.ChainGeneration);

            if (!context.ChainRootId.HasValue || !context.ChainGeneration.HasValue ||
                target.ChainRootId != context.ChainRootId)
                return false;

            return TimeTickers.TryGetValue(context.ChainRootId.Value, out var root) &&
                   root.ParentId == null && root.ChainRootId == root.Id &&
                   root.ChainGeneration == context.ChainGeneration;
        }

        public Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(ReadGraph(() =>
                TimeTickers.TryGetValue(id, out var ticker) ? ticker.Request : null));

        public Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext,
            CancellationToken cancellationToken = default)
        {
            WriteGraph(() =>
            {
                foreach (var id in timeTickerIds)
                {
                    if (TimeTickers.TryGetValue(id, out var ticker))
                    {
                        var updatedTicker = CloneTicker(ticker);
                        ApplyFunctionContextToTicker(updatedTicker, functionContext);
                        TryUpdateTimeTicker(id, updatedTicker, ticker,
                            requireRunnableActivation: !IsFencedTerminalWrite(functionContext));
                    }
                }
                return 0;
            });
            return Task.CompletedTask;
        }

        public Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WriteGraph(() =>
            {
                var winners = new List<Guid>(leases.Count);
                var now = _clock.UtcNow;
                foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
                {
                    while (TimeTickers.TryGetValue(lease.TickerId, out var current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (current.Status != TickerStatus.Queued || current.LockHolder != _lockHolder ||
                            current.AcquisitionToken != lease.AcquisitionToken)
                            break;
                        var updated = CloneTicker(current);
                        updated.Status = TickerStatus.InProgress;
                        updated.UpdatedAt = now;
                        if (!TryUpdateTimeTicker(lease.TickerId, updated, current)) continue;
                        winners.Add(lease.TickerId);
                        break;
                    }
                }
                return winners.ToArray();
            }));
        }

        public Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
        {
            if (ids == null || ids.Length == 0)
                return Task.FromResult(Array.Empty<TimeTickerEntity>());

            return Task.FromResult(WriteGraph(() =>
            {
                var now = _clock.UtcNow;
                var acquired = new List<TimeTickerEntity>();
                foreach (var id in ids)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TimeTickers.TryGetValue(id, out var ticker) || !CanAcquire(ticker))
                        continue;

                    var updatedTicker = CloneTicker(ticker);
                    updatedTicker.LockHolder = _lockHolder;
                    updatedTicker.LockedAt = now;
                    updatedTicker.AcquisitionToken = Guid.NewGuid();
                    updatedTicker.ChainRootId = updatedTicker.Id;
                    updatedTicker.ChainGeneration = updatedTicker.AcquisitionToken;
                    updatedTicker.Status = TickerStatus.InProgress;
                    updatedTicker.UpdatedAt = now;

                    if (TryUpdateTimeTicker(id, updatedTicker, ticker))
                        acquired.Add(ForQueueTimeTickers(updatedTicker));
                }
                return acquired.ToArray();
            }));
        }

        public Task<TimeTickerEntity> AcquireTimeTickerOnDemandAsync(
            Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WriteGraph<TimeTickerEntity>(() =>
            {
                while (TimeTickers.TryGetValue(id, out var ticker))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var eligible = ticker.Status == TickerStatus.Idle ||
                                   (ticker.Status == TickerStatus.Queued &&
                                    (ticker.LockHolder == null || ticker.LockHolder == _lockHolder)) ||
                                   ticker.Status is TickerStatus.Done or TickerStatus.DueDone or
                                       TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped;
                    if (!eligible)
                        return null;

                    var now = _clock.UtcNow;
                    var updated = CloneTicker(ticker);
                    updated.ExecutionTime = executionTime;
                    updated.Status = TickerStatus.InProgress;
                    updated.LockHolder = _lockHolder;
                    updated.LockedAt = now;
                    updated.AcquisitionToken = Guid.NewGuid();
                    updated.ChainRootId = updated.Id;
                    updated.ChainGeneration = updated.AcquisitionToken;
                    updated.RetryCount = 0;
                    updated.ExceptionMessage = null;
                    updated.SkippedReason = null;
                    updated.ExecutedAt = null;
                    updated.ElapsedTime = 0;
                    updated.StaleRestartCount = 0;
                    updated.UpdatedAt = now;
                    if (!TryUpdateTimeTicker(id, updated, ticker))
                        continue;

                    AfterTimeTickerOnDemandMutationForTest?.Invoke(id);
                    TimeTickerResults.TryRemove(id, out _);
                    return ForQueueTimeTickers(updated);
                }

                return null;
            }));
        }

        public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            return ReadGraph(() =>
            {
                if (TimeTickers.TryGetValue(id, out var ticker))
                {
                    var result = BuildTickerHierarchy(ticker);
                    return Task.FromResult(result);
                }

                return Task.FromResult<TTimeTicker>(null);
            });
        }

        public Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();

            // Materialize the full projection under the read lock so a concurrent chain
            // replacement cannot surface a partially swapped aggregate.
            var results = ReadGraph(() =>
            {
                var query = TimeTickers.Values.AsEnumerable();

                if (compiledPredicate != null)
                    query = query.Where(compiledPredicate);

                // Match EF Core - only return root items (ParentId == null) with nested children
                return query
                    .Where(x => x.ParentId == null)  // Only root items, matching EF Core
                    .OrderByDescending(x => x.ExecutionTime)  // Match EF Core's OrderByDescending(x => x.ExecutionTime)
                    .Select(BuildTickerHierarchy)
                    .ToArray();
            });

            return Task.FromResult(results);
        }

        public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();

            // Count and page under the read lock so the total and the materialized page
            // reflect a single consistent graph snapshot, never a mid-replacement view.
            var (items, totalCount) = ReadGraph(() =>
            {
                var query = TimeTickers.Values.AsEnumerable();

                if (compiledPredicate != null)
                    query = query.Where(compiledPredicate);

                // Match EF Core - only count and paginate root items
                query = query.Where(x => x.ParentId == null);

                var count = query.Count();

                var paged = query
                    .OrderByDescending(x => x.ExecutionTime)  // Match EF Core's OrderByDescending(x => x.ExecutionTime)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(BuildTickerHierarchy)
                    .ToArray();

                return (paged, count);
            });

            return Task.FromResult(new PaginationResult<TTimeTicker>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            // Structural insert of whole aggregates: take the write lock so a graph
            // reader (or a concurrent replacement/remove) never observes a partially
            // added root/children set. Matches ReplaceTimeTickerChainAsync fencing.
            var count = WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState())
                    return 0;
                var added = 0;
                foreach (var ticker in tickers)
                {
                    var (rootId, generation) = ResolveInsertedChainIdentity(ticker, tickers);
                    added += AddTickerWithChildren(ticker, chainRootId: rootId,
                        chainGeneration: generation);
                }

                return added;
            });

            return Task.FromResult(count);
        }
        
        private (Guid RootId, Guid? Generation) ResolveInsertedChainIdentity(
            TTimeTicker ticker, IReadOnlyCollection<TTimeTicker> supplied)
        {
            var suppliedById = supplied.ToDictionary(x => x.Id);
            var current = ticker;
            var visited = new HashSet<Guid>();
            while (current.ParentId.HasValue && visited.Add(current.Id))
            {
                if (suppliedById.TryGetValue(current.ParentId.Value, out var suppliedParent))
                {
                    current = suppliedParent;
                    continue;
                }

                if (TimeTickers.TryGetValue(current.ParentId.Value, out var persistedParent))
                    return (persistedParent.ChainRootId ?? persistedParent.Id,
                        persistedParent.ChainGeneration);
                break;
            }

            return (current.Id, current.ChainGeneration);
        }

        private int AddTickerWithChildren(
            TTimeTicker ticker, Guid? parentId = null, Guid? chainRootId = null,
            Guid? chainGeneration = null)
        {
            var count = 0;
            chainRootId ??= ticker.Id;
            ticker.ChainRootId = chainRootId;
            ticker.ChainGeneration = chainGeneration;
            
            // Set the parent ID if this is a child
            if (parentId.HasValue)
            {
                ticker.ParentId = parentId.Value;
            }
            
            // Add the ticker itself
            if (TimeTickers.TryAdd(ticker.Id, ticker))
            {
                // Maintain children index
                if (ticker.ParentId.HasValue)
                    AddChildIndex(ticker.ParentId.Value, ticker.Id);

                count++;
                
                // Recursively add all children
                if (ticker.Children != null && ticker.Children.Count > 0)
                {
                    foreach (var child in ticker.Children)
                    {
                        // Cast to TTimeTicker since Children is ICollection<TTimeTicker>
                        if (child is TTimeTicker childTicker)
                        {
                            count += AddTickerWithChildren(
                                childTicker, ticker.Id, chainRootId, chainGeneration);
                        }
                    }
                }
            }
            
            return count;
        }

        public Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            // Structural update can re-parent nodes and touch the children index, so it
            // runs under the write lock — a graph reader must never see a half-reparented
            // aggregate, and it must not interleave with a concurrent chain replacement.
            var count = WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState())
                    return 0;
                var updated = 0;
                foreach (var ticker in tickers)
                {
                    updated += UpdateTickerWithChildren(ticker);
                }

                return updated;
            });

            return Task.FromResult(count);
        }
        
        private int UpdateTickerWithChildren(TTimeTicker ticker, Guid? parentId = null)
        {
            var count = 0;
            
            // Set the parent ID if this is a child
            if (parentId.HasValue)
            {
                ticker.ParentId = parentId.Value;
            }
            
            // Update the ticker itself
            if (TimeTickers.TryGetValue(ticker.Id, out var existing))
            {
                if (TryUpdateTimeTicker(ticker.Id, ticker, existing))
                {
                    // Maintain children index for parent changes
                    if (existing.ParentId != ticker.ParentId)
                    {
                        if (existing.ParentId.HasValue)
                            RemoveChildIndex(existing.ParentId.Value, ticker.Id);
                        if (ticker.ParentId.HasValue)
                            AddChildIndex(ticker.ParentId.Value, ticker.Id);
                    }

                    count++;
                    
                    // Recursively update all children
                    if (ticker.Children != null && ticker.Children.Count > 0)
                    {
                        foreach (var child in ticker.Children)
                        {
                            // Cast to TTimeTicker since Children is ICollection<TTimeTicker>
                            if (child is TTimeTicker childTicker)
                            {
                                count += UpdateTickerWithChildren(childTicker, ticker.Id);
                            }
                        }
                    }
                }
            }
            else
            {
                // If it doesn't exist, add it (this can happen for new children)
                count += AddTickerWithChildren(ticker, parentId);
            }
            
            return count;
        }

        public Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
        {
            // Cascade removal touches multiple graph entries; hold the write lock so a
            // reader never sees a parent gone while its children linger (torn aggregate)
            // and so it cannot interleave with a concurrent chain replacement.
            var count = WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState())
                    return 0;
                var removedCount = 0;
                var processed = new HashSet<Guid>();
                var now = _clock.UtcNow;
                foreach (var id in tickerIds.Distinct())
                {
                    if (processed.Contains(id) || !TimeTickers.TryGetValue(id, out var requestedRoot) ||
                        requestedRoot.ParentId.HasValue)
                        continue;
                    var aggregate = new List<TTimeTicker>();
                    CollectTimeTickerSubtree(id, aggregate, new HashSet<Guid>());
                    if (aggregate.Any(x => x.Status == TickerStatus.InProgress ||
                                           x.AcquisitionToken.HasValue ||
                                           x.LeaseUntil is { } leaseUntil && leaseUntil > now))
                        continue;

                    foreach (var snapshot in aggregate.AsEnumerable().Reverse())
                    {
                        processed.Add(snapshot.Id);
                        if (!TimeTickers.TryRemove(new KeyValuePair<Guid, TTimeTicker>(snapshot.Id, snapshot)))
                            continue;
                        removedCount++;
                        TimeTickerResults.TryRemove(snapshot.Id, out _);
                        ChildrenIndex.TryRemove(snapshot.Id, out _);
                        if (snapshot.ParentId.HasValue)
                            RemoveChildIndex(snapshot.ParentId.Value, snapshot.Id);
                    }
                }

                return removedCount;
            });

            return Task.FromResult(count);
        }

        // Guards the shared time-ticker graph (TimeTickers + ChildrenIndex) so that
        // structural mutations (chain replacement, add/update/remove-with-children) are
        // observed atomically by graph-traversing readers. A reader holding the read lock
        // can never see a replacement's add-then-remove window (neither a doubled nor a
        // torn aggregate); a structural writer takes the write lock for its whole span.
        // The per-node CAS status writers keep operating lock-free on ConcurrentDictionary
        // entries — they never restructure the parent/child graph — so they are unaffected.
        // Test-only seam invoked while an eligibility-changing CAS holds the read lock.
        // Retention's write lock cannot pass this point until the mutation completes.
        internal static Action<Guid> EligibilityMutationLockHook;
        internal static Action<Guid> BeforeCronOccurrenceMutationLockHook;
        internal static Action DuringGlobalRepairMutationHook;
        internal static Action AfterCronOccurrenceTerminalMutationForTest;
        internal static Action<Guid> AfterTimeTickerOnDemandMutationForTest;
        internal static Action<Guid> AfterAcquisitionMutationForTest;

        private bool IsRunnableActivationState()
        {
            if (IsLegacyAdoptionFenced())
                return false;
            if (_runtimeScopeBindingConfigured)
            {
                if (!_runtimeSchedulerEnabled)
                    return true;
                if (_runtimeActivationScopeKey == null || _reconciliationEpoch <= 0
                    || !ScopedActivationEpochs.TryGetValue(_runtimeActivationScopeKey, out var runtimeState))
                    return false;
                return runtimeState.Epoch == _reconciliationEpoch
                       && (runtimeState.Phase == ActivationEpochPhase.Activated
                           || (runtimeState.Phase == ActivationEpochPhase.Activating
                               && StartupSeederAdmissionContext.Matches(
                                   _runtimeActivationScopeKey, _reconciliationEpoch)));
            }

            var state = _activationEpoch;
            return state == ActivationEpochState.PreEpoch
                   || (state.Epoch == 0 && state.Phase == ActivationEpochPhase.Pending)
                   || (state.Epoch == _reconciliationEpoch
                       && state.Phase == ActivationEpochPhase.Activated);
        }

        private bool IsStructuralMutationActivationState()
        {
            if (IsLegacyAdoptionFenced())
                return false;
            if (!_runtimeScopeBindingConfigured || !_runtimeSchedulerEnabled)
                return true;
            if (_runtimeActivationScopeKey == null || _reconciliationEpoch <= 0
                || !ScopedActivationEpochs.TryGetValue(_runtimeActivationScopeKey, out var state))
                return false;
            return state.Epoch == _reconciliationEpoch
                   && (state.Phase == ActivationEpochPhase.Activated
                       || (state.Phase == ActivationEpochPhase.Activating
                           && StartupSeederAdmissionContext.Matches(
                               _runtimeActivationScopeKey, _reconciliationEpoch)));
        }

        private bool IsLegacyAdoptionFenced()
            => StringComparer.Ordinal.Equals(
                   _runtimePartitionKey, TickerQRuntimePartition.LegacyGlobal.StorageKey)
               && _legacyAdoptionOwner != null;

        private bool TryUpdateTimeTicker(
            Guid id, TTimeTicker updated, TTimeTicker expected, bool requireRunnableActivation = true)
        {
            if (GraphLock.IsReadLockHeld || GraphLock.IsWriteLockHeld)
                return !IsLegacyAdoptionFenced()
                       && (!requireRunnableActivation || IsRunnableActivationState())
                       && TimeTickers.TryUpdate(id, updated, expected);

            GraphLock.EnterReadLock();
            try
            {
                EligibilityMutationLockHook?.Invoke(id);
                return !IsLegacyAdoptionFenced()
                       && (!requireRunnableActivation || IsRunnableActivationState())
                       && TimeTickers.TryUpdate(id, updated, expected);
            }
            finally
            {
                GraphLock.ExitReadLock();
            }
        }

        private bool TryUpdateCronOccurrence(
            Guid id,
            CronTickerOccurrenceEntity<TCronTicker> updated,
            CronTickerOccurrenceEntity<TCronTicker> expected,
            bool requireCurrentDefinition = true,
            bool requireRunnableActivation = true)
        {
            if (GraphLock.IsReadLockHeld || GraphLock.IsWriteLockHeld)
                return !IsLegacyAdoptionFenced()
                       && (!requireRunnableActivation || IsRunnableActivationState())
                       && (!requireCurrentDefinition || IsCurrentCronOccurrence(expected))
                       && CronOccurrences.TryUpdate(id, updated, expected);

            BeforeCronOccurrenceMutationLockHook?.Invoke(id);
            GraphLock.EnterReadLock();
            try
            {
                EligibilityMutationLockHook?.Invoke(id);
                return !IsLegacyAdoptionFenced()
                       && (!requireRunnableActivation || IsRunnableActivationState())
                       && (!requireCurrentDefinition || IsCurrentCronOccurrence(expected))
                       && CronOccurrences.TryUpdate(id, updated, expected);
            }
            finally
            {
                GraphLock.ExitReadLock();
            }
        }

        // Runs a graph read under the shared read lock. Callees must be lock-free (they
        // are: BuildTickerHierarchy / ForQueueTimeTickers and their private helpers).
        private T ReadGraph<T>(Func<T> read)
        {
            GraphLock.EnterReadLock();
            try
            {
                return read();
            }
            finally
            {
                GraphLock.ExitReadLock();
            }
        }

        // Runs a structural graph mutation under the exclusive write lock.
        private T WriteGraph<T>(Func<T> write)
        {
            GraphLock.EnterWriteLock();
            try
            {
                if (IsLegacyAdoptionFenced())
                    throw new InvalidOperationException(
                        "Legacy runtime state is fenced by runtime partition adoption.");
                return write();
            }
            finally
            {
                GraphLock.ExitWriteLock();
            }
        }

        // Test-only seam. Invoked while the exclusive write lock is held, after the
        // replacement aggregate has been fully inserted but before the original is
        // removed — i.e. exactly the window in which the graph momentarily holds BOTH
        // the old and new roots. A deterministic concurrency regression sets this to
        // launch a reader and prove ReadGraph fences it out of the torn window. Null
        // and zero-cost in production.
        internal static Action GraphReplacementMidpointHook;

        public Task<int> ReplaceTimeTickerChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default)
        {
            if (newRoot == null)
                throw new ArgumentNullException(nameof(newRoot));

            return WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState())
                    return Task.FromResult(0);
                if (!TimeTickers.TryGetValue(oldRootId, out var oldRoot) || oldRoot.ParentId.HasValue)
                    return Task.FromResult(0);
                var oldAggregate = new List<TTimeTicker>();
                CollectTimeTickerSubtree(oldRootId, oldAggregate, new HashSet<Guid>());
                var now = _clock.UtcNow;
                if (oldAggregate.Any(x => x.Status == TickerStatus.InProgress ||
                                          x.AcquisitionToken.HasValue ||
                                          (x.LeaseUntil.HasValue && x.LeaseUntil > now)))
                    return Task.FromResult(0);

                // Persist the COMPLETE replacement first. Fail closed on any id collision
                // BEFORE removing anything, so the original aggregate is never lost.
                var replacementIds = new HashSet<Guid>();
                CollectChainIds(newRoot, replacementIds);
                foreach (var id in replacementIds)
                {
                    if (TimeTickers.ContainsKey(id))
                        throw new InvalidOperationException(
                            $"Cannot replace chain: a ticker with id {id} already exists.");
                }

                NormalizeReplacementChain(newRoot, null, newRoot.Id);

                var insertedIds = new List<Guid>(replacementIds.Count);
                try
                {
                    AddReplacementWithChildren(newRoot, parentId: null, insertedIds);
                }
                catch
                {
                    // Remove only rows this replacement attempt inserted. The original graph
                    // was not touched yet, so failure is fully atomic even under a racing add.
                    for (var i = insertedIds.Count - 1; i >= 0; i--)
                    {
                        if (TimeTickers.TryRemove(insertedIds[i], out var removed) && removed.ParentId.HasValue)
                            RemoveChildIndex(removed.ParentId.Value, removed.Id);
                    }
                    throw;
                }

                // Both aggregates are momentarily present here (write lock still held).
                GraphReplacementMidpointHook?.Invoke();

                // Only once the replacement is in place do we remove the original aggregate.
                RemoveAggregateCascade(oldRootId);

                return Task.FromResult(insertedIds.Count);
            });
        }

        private static void CollectChainIds(TTimeTicker node, HashSet<Guid> ids)
        {
            if (!ids.Add(node.Id))
                throw new InvalidOperationException(
                    $"Cannot replace chain: replacement contains duplicate ticker id {node.Id}.");
            if (node.Children == null)
                return;
            foreach (var child in node.Children)
                if (child is TTimeTicker typedChild)
                    CollectChainIds(typedChild, ids);
        }

        private static void NormalizeReplacementChain(TTimeTicker node, Guid? parentId, Guid rootId)
        {
            node.ParentId = parentId;
            node.ChainRootId = rootId;
            node.Status = TickerStatus.Idle;
            node.LockHolder = null;
            node.LockedAt = null;
            node.LeaseUntil = null;
            node.AcquisitionToken = null;
            node.ChainGeneration = null;
            node.ExecutedAt = null;
            node.ExceptionMessage = null;
            node.SkippedReason = null;
            node.ElapsedTime = 0;
            node.RetryCount = 0;
            node.StaleRestartCount = 0;
            if (node.Children == null) return;
            foreach (var child in node.Children)
                if (child is TTimeTicker typedChild)
                    NormalizeReplacementChain(typedChild, node.Id, rootId);
        }

        private void AddReplacementWithChildren(
            TTimeTicker ticker,
            Guid? parentId,
            List<Guid> insertedIds)
        {
            if (parentId.HasValue)
                ticker.ParentId = parentId.Value;

            if (!TimeTickers.TryAdd(ticker.Id, ticker))
                throw new InvalidOperationException(
                    $"Cannot replace chain: a ticker with id {ticker.Id} was added concurrently.");

            insertedIds.Add(ticker.Id);
            if (ticker.ParentId.HasValue)
                AddChildIndex(ticker.ParentId.Value, ticker.Id);

            if (ticker.Children == null)
                return;

            foreach (var child in ticker.Children)
                if (child is TTimeTicker typedChild)
                    AddReplacementWithChildren(typedChild, ticker.Id, insertedIds);
        }

        private void RemoveAggregateCascade(Guid rootId)
        {
            // Depth-first so descendants at every level are removed, not just direct children.
            foreach (var childId in GetChildrenIds(rootId))
                RemoveAggregateCascade(childId);

            if (TimeTickers.TryRemove(rootId, out var removed))
            {
                TimeTickerResults.TryRemove(rootId, out _);
                ChildrenIndex.TryRemove(rootId, out _);
                if (removed.ParentId.HasValue)
                    RemoveChildIndex(removed.ParentId.Value, removed.Id);
            }
        }

        private void CollectTimeTickerSubtree(
            Guid id, ICollection<TTimeTicker> aggregate, ISet<Guid> visited)
        {
            if (!visited.Add(id) || !TimeTickers.TryGetValue(id, out var ticker))
                return;
            aggregate.Add(ticker);
            foreach (var childId in GetChildrenIds(id))
                CollectTimeTickerSubtree(childId, aggregate, visited);
        }

        public Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            var releasable = TimeTickers.Values
                .Where(x =>
                    (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                    x.LockHolder == instanceIdentifier && x.AcquisitionToken.HasValue)
                .ToArray();

            foreach (var ticker in releasable)
            {
                if (!TimeTickers.TryGetValue(ticker.Id, out var currentTicker))
                    continue;
                if (currentTicker.Status is not (TickerStatus.Idle or TickerStatus.Queued) ||
                    currentTicker.LockHolder != instanceIdentifier || !currentTicker.AcquisitionToken.HasValue)
                    continue;

                var updatedTicker = CloneTicker(currentTicker);
                updatedTicker.LockHolder = null;
                updatedTicker.LockedAt = null;
                updatedTicker.LeaseUntil = null;
                updatedTicker.AcquisitionToken = null;
                updatedTicker.ChainGeneration = null;
                updatedTicker.Status = TickerStatus.Idle;
                updatedTicker.UpdatedAt = now;

                TryUpdateTimeTicker(ticker.Id, updatedTicker, currentTicker);
            }

            var inProgress = TimeTickers.Values
                .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress &&
                            x.AcquisitionToken.HasValue)
                .ToArray();

            foreach (var ticker in inProgress)
            {
                if (!TimeTickers.TryGetValue(ticker.Id, out var currentTicker))
                    continue;
                if (currentTicker.Status != TickerStatus.InProgress ||
                    currentTicker.LockHolder != instanceIdentifier || !currentTicker.AcquisitionToken.HasValue)
                    continue;

                var updatedTicker = CloneTicker(currentTicker);
                updatedTicker.LockHolder = null;
                updatedTicker.LockedAt = null;
                updatedTicker.LeaseUntil = null;
                updatedTicker.AcquisitionToken = null;
                updatedTicker.ChainGeneration = null;
                updatedTicker.Status = TickerStatus.Skipped;
                updatedTicker.SkippedReason = "Node is not alive!";
                updatedTicker.ExecutedAt = now;
                updatedTicker.UpdatedAt = now;

                TryUpdateTimeTicker(ticker.Id, updatedTicker, currentTicker,
                    requireRunnableActivation: false);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Cron Ticker Methods

        public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
            => MigrateDefinedCronTickers(
                Array.ConvertAll(cronTickers, static ticker => new DefinedCronTickerSeed(ticker.Function, ticker.Expression)),
                cancellationToken);

        public Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
            => MigrateDefinedCronTickers(new DefinedCronSeedManifest(cronTickers), cancellationToken);

        public Task MigrateDefinedCronTickers(DefinedCronSeedManifest manifest, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RuntimeManifestAdmission.Validate(manifest, _runtimeScopeBindingConfigured,
                _runtimeSchedulerEnabled, _runtimeActivationScopeKey, _reconciliationEpoch);
            var now = _clock.UtcNow;
            var grace = _retirementGracePeriod;

            // Orphan detection compares persisted SEEDED rows (non-empty InitIdentifier) to the DESIRED
            // SEED MANIFEST — the local code-owned schedules this pass wants — never to the global runtime
            // function registry. Comparing to the registry conflated "function still registered" with
            // "code still wants a seeded schedule", so removing only a cron expression left the stale
            // seeded row firing forever (Slice 1). Dashboard-created crons (null/empty InitIdentifier),
            // including those targeting SDK/remote `name@node` functions the initializer never seeds, are
            // never candidates. See the EF provider's mirror comment for the full rationale.
            var blockedFunctions = manifest.Seeds.Where(s => !s.CanSeed)
                .Select(s => s.Function).ToHashSet(StringComparer.Ordinal);

            // The whole reconcile runs under the exclusive graph write lock so duplicate selection and the
            // retirement/adoption mutations are safe under concurrent reconciliation (the store is
            // process-wide static state shared by every provider instance in this process).
            WriteGraph<object>(() =>
            {
                // A pre-namespace row can be transferred only when both the persisted candidate and the
                // local definition are singular. This check happens before any retirement/adoption write.
                if (!manifest.IsLegacyGlobal)
                {
                    foreach (var functionGroup in manifest.Seeds.Where(s => s.CanSeed).GroupBy(s => s.Function))
                    {
                        var documentedLegacyKeys = functionGroup
                            .SelectMany(seed => CronSeedIdentity.LegacyAdoptionKeys(
                                manifest.ApplicationNamespace, seed.StableDefinitionId))
                            .ToHashSet(StringComparer.Ordinal);
                        var legacy = CronTickers.Values.Where(x =>
                                !string.IsNullOrEmpty(x.InitIdentifier)
                                && CronSeedIdentity.CanonicallyEquals(x.Function, functionGroup.Key)
                                && x.SeedOwnerNamespace == null
                                && (x.SeedKey == null || CronSeedIdentity.CanonicallyEquals(x.SeedKey, x.Function)
                                    || documentedLegacyKeys.Contains(x.SeedKey)))
                            .ToArray();
                        if (legacy.Length == 0)
                            continue;
                        if (!manifest.TryGetLegacyOwner(functionGroup.Key, out var explicitOwner))
                            throw new InvalidOperationException(
                                $"Legacy defined-Cron function '{functionGroup.Key}' requires an explicit legacy ownership mapping before any application may mutate it.");
                        if (!string.Equals(explicitOwner, manifest.ApplicationNamespace, StringComparison.Ordinal))
                            continue;
                        if (legacy.Length != 1 || functionGroup.Count() != 1)
                            throw new InvalidOperationException(
                                $"Ambiguous legacy defined-Cron ownership for function '{functionGroup.Key}' while application namespace " +
                                $"'{manifest.ApplicationNamespace}' attempted adoption. No rows were mutated; resolve ownership explicitly.");
                    }
                }

                // Phase A — non-destructive retirement of seeded rows no longer desired. A blocked
                // required-contract seed retires immediately; an absent seed honors the grace window. Rows
                // are NEVER removed here (Slice 3) — only their retirement bookkeeping is updated.
                foreach (var ticker in CronTickers.Values.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(ticker.InitIdentifier)
                        || (manifest.IsLegacyGlobal
                            ? !manifest.IsOrphanedSeedFunction(ticker.Function)
                            : !CronSeedIdentity.CanonicallyEquals(
                                  ticker.SeedOwnerNamespace, manifest.ApplicationNamespace)
                              || !manifest.IsOrphanedSeedKey(ticker.SeedKey)))
                        continue;

                    var immediate = blockedFunctions.Any(function =>
                        CronSeedIdentity.CanonicallyEquals(function, ticker.Function));
                    if (CronSeedRetirement.ApplyRetirement(ticker, now, grace, immediate))
                        ticker.UpdatedAt = now;
                    if (ticker.RetiredAt.HasValue)
                        RemoveUnleasedPendingCronOccurrences(ticker.Id, now);
                }

                foreach (var seed in manifest.Seeds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!seed.CanSeed)
                        continue;

                    var seedKey = manifest.SeedKeyFor(seed);
                    var documentedLegacyKeys = manifest.IsLegacyGlobal
                        ? Array.Empty<string>()
                        : CronSeedIdentity.LegacyAdoptionKeys(
                            manifest.ApplicationNamespace, seed.StableDefinitionId);
                    var acceptedSeedKeys = manifest.IsLegacyGlobal
                        ? Array.Empty<string>()
                        : CronSeedIdentity.AcceptedSeedKeys(
                            manifest.ApplicationNamespace, seed.StableDefinitionId);

                    // Phase B — reconcile the seeded rows for this function keyed by the stable SeedKey.
                    // Duplicate legacy seeded rows are duplicate-tolerant: the deterministic canonical row
                    // (lowest id) reconciles IN PLACE while every redundant duplicate is disabled and
                    // marked retired IN PLACE (never deleted). A user/dashboard row sharing the function
                    // name (null/non-seed InitIdentifier) is never touched.
                    var group = CronTickers.Values.ToArray()
                        .Where(x => !string.IsNullOrEmpty(x.InitIdentifier)
                            && (manifest.IsLegacyGlobal
                                ? CronSeedIdentity.CanonicallyEquals(x.Function, seed.Function)
                                : (acceptedSeedKeys.Contains(x.SeedKey, StringComparer.Ordinal)
                                   && CronSeedIdentity.CanonicallyEquals(
                                       x.SeedOwnerNamespace, manifest.ApplicationNamespace))
                                  || (CronSeedIdentity.CanonicallyEquals(x.Function, seed.Function)
                                      && x.SeedOwnerNamespace == null
                                      && manifest.MayAdoptLegacy(seed.Function)
                                      && (x.SeedKey == null || CronSeedIdentity.CanonicallyEquals(x.SeedKey, x.Function)
                                          || documentedLegacyKeys.Contains(x.SeedKey, StringComparer.Ordinal)))))
                        .OrderBy(x => x.Id)
                        .ToArray();

                    if (group.Length > 0)
                    {
                        // Prefer the row that already owns this SeedKey, then an active row, then lowest id;
                        // picking the lowest id blindly would move an already-owned SeedKey onto a legacy
                        // null-key duplicate and produce two enabled keyed owners.
                        var (canonical, duplicates) = CronSeedCanonical.Select(group, seedKey);
                        var definitionChanged = false;

                        // Adopt the stable SeedKey onto a legacy row IN PLACE (never re-keys the row).
                        if (canonical.SeedKey == null)
                        {
                            canonical.SeedKey = seedKey;
                            canonical.UpdatedAt = now;
                        }
                        else if (!manifest.IsLegacyGlobal && !string.Equals(canonical.SeedKey, seedKey, StringComparison.Ordinal))
                        {
                            canonical.SeedKey = seedKey;
                            canonical.UpdatedAt = now;
                        }

                        if (!manifest.IsLegacyGlobal
                            && !string.Equals(canonical.SeedOwnerNamespace, manifest.ApplicationNamespace, StringComparison.Ordinal))
                        {
                            canonical.SeedOwnerNamespace = manifest.ApplicationNamespace;
                            canonical.UpdatedAt = now;
                        }

                        if (!string.Equals(canonical.Expression, seed.Expression, StringComparison.Ordinal))
                        {
                            canonical.Expression = seed.Expression;
                            canonical.UpdatedAt = now;
                            definitionChanged = true;
                        }

                        // Reconcile authoritative contract identity onto seeded rows only; legacy/dashboard
                        // rows (no seed InitIdentifier) keep their own identity.
                        if (!string.IsNullOrEmpty(canonical.InitIdentifier)
                            && !seed.MatchesIdentity(canonical.RequestContractVersion, canonical.RequestContractFingerprint))
                        {
                            canonical.RequestContractVersion = seed.RequestContractVersion;
                            canonical.RequestContractFingerprint = seed.RequestContractFingerprint;
                            canonical.UpdatedAt = now;
                            definitionChanged = true;
                        }

                        if (canonical.Retries != seed.Retries)
                        {
                            canonical.Retries = seed.Retries;
                            definitionChanged = true;
                        }
                        if (!(canonical.RetryIntervals ?? Array.Empty<int>()).SequenceEqual(
                                seed.RetryIntervals ?? Array.Empty<int>()))
                        {
                            canonical.RetryIntervals = seed.RetryIntervals;
                            definitionChanged = true;
                        }
                        if (canonical.TimeoutSeconds != seed.TimeoutSeconds)
                        {
                            canonical.TimeoutSeconds = seed.TimeoutSeconds;
                            definitionChanged = true;
                        }

                        if (definitionChanged)
                        {
                            canonical.DefinitionRevision = Math.Max(1, canonical.DefinitionRevision + 1);
                            canonical.UpdatedAt = now;
                        }
                        else if (canonical.DefinitionRevision <= 0)
                        {
                            canonical.DefinitionRevision = 1;
                            canonical.UpdatedAt = now;
                        }

                        // Desired-active seed: clear any framework retirement, restoring only framework-disabled state.
                        if (CronSeedRetirement.ClearRetirement(canonical))
                            canonical.UpdatedAt = now;

                        // Advisory heartbeat for retirement grace accounting.
                        canonical.SeedLastSeenAt = now;
                        if (definitionChanged)
                            RemoveUnleasedPendingCronOccurrences(canonical.Id, now);

                        // Retire redundant duplicates in place (canonical already chosen); never delete.
                        foreach (var dup in duplicates)
                        {
                            if (CronSeedRetirement.RetireDuplicate(dup, now))
                                dup.UpdatedAt = now;
                            if (dup.RetiredAt.HasValue)
                                RemoveUnleasedPendingCronOccurrences(dup.Id, now);
                        }
                    }
                    else
                    {
                        var id = CronSeedIdentity.DeterministicId(seedKey);
                        var cronTicker = new TCronTicker
                        {
                            Id = id,
                            Function = seed.Function,
                            Expression = seed.Expression,
                            DefinitionRevision = 1,
                            SeedKey = seedKey,
                            SeedOwnerNamespace = manifest.ApplicationNamespace,
                            SeedLastSeenAt = now,
                            InitIdentifier = $"MemoryTicker_Seeded_{seed.Function}",
                            CreatedAt = now,
                            UpdatedAt = now,
                            Request = Array.Empty<byte>(),
                            RequestContractVersion = seed.RequestContractVersion,
                            RequestContractFingerprint = seed.RequestContractFingerprint,
                            Retries = seed.Retries,
                            RetryIntervals = seed.RetryIntervals,
                            TimeoutSeconds = seed.TimeoutSeconds
                        };

                        // Atomic converge: a concurrent reconcile computing the same deterministic id wins
                        // the slot and both observers see exactly one row.
                        CronTickers.GetOrAdd(id, cronTicker);
                    }
                }

                return null;
            });

            return Task.CompletedTask;
        }

        private void RemoveUnleasedPendingCronOccurrences(Guid cronTickerId, DateTime now)
        {
            foreach (var occurrence in CronOccurrences.Values
                         .Where(x => x.CronTickerId == cronTickerId
                                     && x.Status is TickerStatus.Idle or TickerStatus.Queued
                                     && string.IsNullOrEmpty(x.LockHolder)
                                     && !x.AcquisitionToken.HasValue
                                     && (!x.LeaseUntil.HasValue || x.LeaseUntil <= now))
                         .ToArray())
            {
                if (!CronOccurrences.TryGetValue(occurrence.Id, out var current))
                    continue;
                var quarantined = CloneCronOccurrence(current);
                quarantined.Status = TickerStatus.Skipped;
                quarantined.SkippedReason =
                    "Quarantined because its Cron definition revision is stale after reconciliation.";
                quarantined.ExecutedAt ??= now;
                quarantined.LockHolder = null;
                quarantined.LockedAt = null;
                quarantined.LeaseUntil = null;
                quarantined.AcquisitionToken = null;
                quarantined.UpdatedAt = now;
                if (TryUpdateCronOccurrence(occurrence.Id, quarantined, current,
                        requireCurrentDefinition: false, requireRunnableActivation: false))
                {
                    var indexKey = (current.ExecutionTime, current.CronTickerId);
                    if (CronOccurrenceIndex.TryGetValue(indexKey, out var ownerId)
                        && ownerId == current.Id)
                        CronOccurrenceIndex.TryRemove(indexKey, out _);
                }
            }
        }

        public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
        {
            var result = ReadGraph(() => CronTickers.Values
                    .Where(x => x.IsEnabled && !x.IsSystemPaused)
                    .Cast<CronTickerEntity>()
                    .ToArray());

            return Task.FromResult(result);
        }

        public Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadGraph(() =>
                CronTickers.TryGetValue(id, out var ticker) ? ticker : null));
        }

        public Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            var compiledPredicate = predicate?.Compile();
            var results = ReadGraph(() =>
            {
                var query = CronTickers.Values.AsEnumerable();
                if (compiledPredicate != null)
                    query = query.Where(compiledPredicate);
                return query.OrderByDescending(x => x.CreatedAt).ToArray();
            });

            return Task.FromResult(results);
        }

        public Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            var snapshot = ReadGraph(() =>
            {
                var query = CronTickers.Values.AsEnumerable();
                if (compiledPredicate != null)
                    query = query.Where(compiledPredicate);
                var materialized = query.OrderByDescending(x => x.CreatedAt).ToArray();
                return (Items: materialized.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray(),
                    TotalCount: materialized.Length);
            });

            return Task.FromResult(new PaginationResult<TCronTicker>
            {
                Items = snapshot.Items,
                TotalCount = snapshot.TotalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            return Task.FromResult(WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState()) return 0;
                var count = 0;
                foreach (var ticker in tickers)
                    if (CronTickers.TryAdd(ticker.Id, ticker)) count++;
                return count;
            }));
        }

        public Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
        {
            return Task.FromResult(WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState()) return 0;
                var count = 0;
                foreach (var ticker in cronTicker)
                {
                    if (CronTickers.TryGetValue(ticker.Id, out var existing)
                        && CronTickers.TryUpdate(ticker.Id, ticker, existing))
                        count++;
                }
                return count;
            }));
        }

        public Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            return Task.FromResult(WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState()) return 0;
                var count = 0;
                foreach (var id in cronTickerIds)
                    if (CronTickers.TryRemove(id, out _)) count++;
                return count;
            }));
        }

        #endregion

        #region Cron Occurrence Methods

        public Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReadGraph(() =>
            {
                if (!IsRunnableActivationState())
                    return null;

                var now = _clock.UtcNow;
                var mainSchedulerThreshold = now.AddSeconds(-1);
                var query = CronOccurrences.Values.AsEnumerable();

                if (ids != null && ids.Length > 0)
                    query = query.Where(x => ids.Contains(x.CronTickerId));

                foreach (var stale in query.Where(x => !IsCurrentCronOccurrence(x)).ToArray())
                    QuarantineStaleCronOccurrence(stale, now);

                return query
                    .Where(x => CanAcquireCronOccurrence(x))
                    .Where(IsCurrentCronOccurrence)
                    .Where(x => x.ExecutionTime >= mainSchedulerThreshold)
                    .OrderBy(x => x.ExecutionTime)
                    .FirstOrDefault();
            }));
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await AcquisitionPublicationGate.WaitAsync(cancellationToken);
            try
            {
                var now = _clock.UtcNow;
                foreach (var context in cronTickerOccurrences.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var occurrenceId = context.NextCronOccurrence?.Id ?? Guid.NewGuid();
                    CronTickerOccurrenceEntity<TCronTicker> queued = null;

                    BeforeCronOccurrenceMutationLockHook?.Invoke(occurrenceId);
                    GraphLock.EnterReadLock();
                    try
                    {
                        // Activation, authoritative parent revision, uniqueness, and graph mutation share one
                        // boundary. No activation or definition publication can become visible between them.
                        if (!IsRunnableActivationState()
                            || !CronTickers.TryGetValue(context.Id, out var authoritative)
                            || context.DefinitionRevision != authoritative.DefinitionRevision)
                        continue;

                        if (CronOccurrences.TryGetValue(occurrenceId, out var existingOccurrence))
                        {
                            if (!IsCurrentCronOccurrence(existingOccurrence))
                            {
                                QuarantineStaleCronOccurrence(existingOccurrence, now);
                                continue;
                            }

                            var updatedOccurrence = CloneCronOccurrence(existingOccurrence);
                            updatedOccurrence.LockHolder = _lockHolder;
                            updatedOccurrence.LockedAt = now;
                            updatedOccurrence.AcquisitionToken = Guid.NewGuid();
                            updatedOccurrence.UpdatedAt = now;
                            updatedOccurrence.Status = TickerStatus.Queued;
                            if (TryUpdateCronOccurrence(occurrenceId, updatedOccurrence, existingOccurrence))
                                queued = updatedOccurrence;
                        }
                        else
                        {
                            var indexKey = (cronTickerOccurrences.Key, context.Id);
                            if (!CronOccurrenceIndex.TryAdd(indexKey, occurrenceId))
                                continue;

                            var newOccurrence = new CronTickerOccurrenceEntity<TCronTicker>
                            {
                                Id = occurrenceId,
                                CronTickerId = context.Id,
                                DefinitionRevision = authoritative.DefinitionRevision,
                                ExecutionTime = cronTickerOccurrences.Key,
                                Status = TickerStatus.Queued,
                                LockHolder = _lockHolder,
                                LockedAt = now,
                                AcquisitionToken = Guid.NewGuid(),
                                CreatedAt = context.NextCronOccurrence?.CreatedAt ?? now,
                                UpdatedAt = now,
                                RetryCount = 0,
                                CronTicker = authoritative
                            };

                            if (CronOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                                queued = newOccurrence;
                            else
                                CronOccurrenceIndex.TryRemove(indexKey, out _);
                        }
                    }
                    finally
                    {
                        GraphLock.ExitReadLock();
                    }

                    if (queued != null)
                    {
                        AfterAcquisitionMutationForTest?.Invoke(queued.Id);
                        yield return queued;
                    }
                }
            }
            finally
            {
                AcquisitionPublicationGate.Release();
            }
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await AcquisitionPublicationGate.WaitAsync(cancellationToken);
            try
            {
                var now = _clock.UtcNow;
                var fallbackThreshold = now.AddSeconds(-1);
                var occurrencesToUpdate = CronOccurrences.Values
                    .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                    .Where(x => x.ExecutionTime <= fallbackThreshold)
                    .ToArray();

                foreach (var occurrence in occurrencesToUpdate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCurrentCronOccurrence(occurrence))
                    {
                        QuarantineStaleCronOccurrence(occurrence, now);
                        continue;
                    }

                    if (CronOccurrences.TryGetValue(occurrence.Id, out var existingOccurrence)
                        && existingOccurrence.UpdatedAt <= occurrence.UpdatedAt)
                    {
                        var updatedOccurrence = CloneCronOccurrence(existingOccurrence);
                        updatedOccurrence.LockHolder = _lockHolder;
                        updatedOccurrence.LockedAt = now;
                        updatedOccurrence.AcquisitionToken = Guid.NewGuid();
                        updatedOccurrence.UpdatedAt = now;
                        updatedOccurrence.Status = TickerStatus.InProgress;

                        if (TryUpdateCronOccurrence(occurrence.Id, updatedOccurrence, existingOccurrence))
                        {
                            AfterAcquisitionMutationForTest?.Invoke(occurrence.Id);
                            yield return updatedOccurrence;
                        }
                    }
                }
            }
            finally
            {
                AcquisitionPublicationGate.Release();
            }
        }

        public Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            return WriteGraph(() =>
            {
                if (CronOccurrences.TryGetValue(functionContext.TickerId, out var occurrence))
                {
                    if (IsFencedTerminalWrite(functionContext) &&
                        (!functionContext.AcquisitionToken.HasValue || occurrence.LockHolder != _lockHolder ||
                         occurrence.AcquisitionToken != functionContext.AcquisitionToken))
                        return Task.CompletedTask;

                    var updatedOccurrence = CloneCronOccurrence(occurrence);
                    ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);

                    EligibilityMutationLockHook?.Invoke(functionContext.TickerId);
                    if (TryUpdateCronOccurrence(functionContext.TickerId, updatedOccurrence, occurrence,
                            requireRunnableActivation: !IsFencedTerminalWrite(functionContext)))
                    {
                        AfterCronOccurrenceTerminalMutationForTest?.Invoke();
                        if (IsSuccessfulResultWrite(functionContext))
                            CronOccurrenceResults[functionContext.TickerId] = functionContext.ResultEnvelope;
                    }
                }

                return Task.CompletedTask;
            });
        }

        public Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var idsToRelease = occurrenceIds == null || occurrenceIds.Length == 0
                ? CronOccurrences.Keys.ToArray() 
                : occurrenceIds;

            foreach (var id in idsToRelease)
            {
                if (CronOccurrences.TryGetValue(id, out var occurrence))
                {
                    if (CanReleaseOwned(occurrence))
                    {
                        var updatedOccurrence = CloneCronOccurrence(occurrence);
                        updatedOccurrence.LockHolder = null;
                        updatedOccurrence.LockedAt = null;
                        updatedOccurrence.LeaseUntil = null;
                        updatedOccurrence.AcquisitionToken = null;
                        updatedOccurrence.Status = TickerStatus.Idle;
                        updatedOccurrence.UpdatedAt = now;

                        TryUpdateCronOccurrence(id, updatedOccurrence, occurrence);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReadGraph(() =>
            {
                if (CronOccurrences.TryGetValue(tickerId, out var occurrence))
                {
                    if (occurrence.CronTicker != null)
                        return occurrence.CronTicker.Request;

                    if (CronTickers.TryGetValue(occurrence.CronTickerId, out var cronTicker))
                        return cronTicker.Request;
                }

                return null;
            }));
        }

        public Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            foreach (var id in timeTickerIds)
            {
                if (CronOccurrences.TryGetValue(id, out var occurrence))
                {
                    var updatedOccurrence = CloneCronOccurrence(occurrence);
                    ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);
                    TryUpdateCronOccurrence(id, updatedOccurrence, occurrence,
                        requireRunnableActivation: !IsFencedTerminalWrite(functionContext));
                }
            }
            
            return Task.CompletedTask;
        }

        public Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            var candidates = leases.Where(x => x.AcquisitionToken.HasValue).Distinct().ToArray();
            var currentAtMutationBoundary = candidates.Where(candidate =>
                    CronOccurrences.TryGetValue(candidate.TickerId, out var occurrence)
                    && occurrence.Status == TickerStatus.Queued
                    && occurrence.LockHolder == _lockHolder
                    && occurrence.AcquisitionToken == candidate.AcquisitionToken
                    && IsCurrentCronOccurrence(occurrence))
                .Select(candidate => candidate.TickerId)
                .ToHashSet();
            foreach (var id in currentAtMutationBoundary)
                BeforeCronOccurrenceMutationLockHook?.Invoke(id);

            return Task.FromResult(WriteGraph(() =>
            {
                var winners = new List<Guid>(candidates.Length);
                var now = _clock.UtcNow;
                foreach (var lease in candidates)
                {
                    while (CronOccurrences.TryGetValue(lease.TickerId, out var current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (current.Status != TickerStatus.Queued || current.LockHolder != _lockHolder ||
                            current.AcquisitionToken != lease.AcquisitionToken)
                            break;
                        if (!IsCurrentCronOccurrence(current))
                        {
                            if (!currentAtMutationBoundary.Contains(lease.TickerId))
                                QuarantineStaleCronOccurrence(current, now);
                            break;
                        }
                        var updated = CloneCronOccurrence(current);
                        updated.Status = TickerStatus.InProgress;
                        updated.UpdatedAt = now;
                        if (!TryUpdateCronOccurrence(lease.TickerId, updated, current)) continue;
                        winners.Add(lease.TickerId);
                        break;
                    }
                }
                return winners.ToArray();
            }));
        }

        public Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            var releasable = CronOccurrences.Values
                .Where(x =>
                    (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                    x.LockHolder == instanceIdentifier && x.AcquisitionToken.HasValue)
                .ToArray();

            foreach (var occurrence in releasable)
            {
                if (!CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
                    continue;
                if (currentOccurrence.Status is not (TickerStatus.Idle or TickerStatus.Queued) ||
                    currentOccurrence.LockHolder != instanceIdentifier ||
                    !currentOccurrence.AcquisitionToken.HasValue)
                    continue;

                var updatedOccurrence = CloneCronOccurrence(currentOccurrence);
                updatedOccurrence.LockHolder = null;
                updatedOccurrence.LockedAt = null;
                updatedOccurrence.LeaseUntil = null;
                updatedOccurrence.AcquisitionToken = null;
                updatedOccurrence.Status = TickerStatus.Idle;
                updatedOccurrence.UpdatedAt = now;

                TryUpdateCronOccurrence(occurrence.Id, updatedOccurrence, currentOccurrence);
            }

            var inProgress = CronOccurrences.Values
                .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress &&
                            x.AcquisitionToken.HasValue)
                .ToArray();

            foreach (var occurrence in inProgress)
            {
                if (!CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
                    continue;
                if (currentOccurrence.Status != TickerStatus.InProgress ||
                    currentOccurrence.LockHolder != instanceIdentifier ||
                    !currentOccurrence.AcquisitionToken.HasValue)
                    continue;

                var updatedOccurrence = CloneCronOccurrence(currentOccurrence);
                updatedOccurrence.LockHolder = null;
                updatedOccurrence.LockedAt = null;
                updatedOccurrence.LeaseUntil = null;
                updatedOccurrence.AcquisitionToken = null;
                updatedOccurrence.Status = TickerStatus.Skipped;
                updatedOccurrence.SkippedReason = "Node is not alive!";
                updatedOccurrence.ExecutedAt = now;
                updatedOccurrence.UpdatedAt = now;

                TryUpdateCronOccurrence(occurrence.Id, updatedOccurrence, currentOccurrence,
                    requireRunnableActivation: false);
            }

            return Task.CompletedTask;
        }

        public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            return Task.FromResult(ReadGraph(() =>
            {
                var query = CronOccurrences.Values.AsEnumerable();
                if (compiledPredicate != null) query = query.Where(compiledPredicate);
                return query.OrderByDescending(x => x.CreatedAt).ToArray();
            }));
        }

        public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            return Task.FromResult(ReadGraph(() =>
            {
                var query = CronOccurrences.Values.AsEnumerable();
                if (compiledPredicate != null) query = query.Where(compiledPredicate);
                var rows = query.OrderByDescending(x => x.CreatedAt).ToArray();
                return new PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>
                {
                    Items = rows.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray(),
                    TotalCount = rows.Length,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }));
        }

        public Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            var now = _clock.UtcNow;
            var count = WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState())
                    return 0;

                var inserted = 0;
                foreach (var occurrence in cronTickerOccurrences)
                {
                    if (!CronTickers.TryGetValue(occurrence.CronTickerId, out var cronTicker))
                        continue;
                    occurrence.CronTicker = cronTicker;
                    if (occurrence.DefinitionRevision <= 0)
                        occurrence.DefinitionRevision = cronTicker.DefinitionRevision;
                    if (occurrence.DefinitionRevision != cronTicker.DefinitionRevision &&
                        occurrence.Status is TickerStatus.Idle or TickerStatus.Queued)
                    {
                        occurrence.Status = TickerStatus.Skipped;
                        occurrence.SkippedReason = "Quarantined because its Cron definition revision is stale.";
                        occurrence.ExecutedAt ??= now;
                        occurrence.LockHolder = null;
                        occurrence.LockedAt = null;
                        occurrence.LeaseUntil = null;
                        occurrence.AcquisitionToken = null;
                        occurrence.UpdatedAt = now;
                    }

                    var indexKey = (occurrence.ExecutionTime, occurrence.CronTickerId);
                    if (CronOccurrenceIndex.TryAdd(indexKey, occurrence.Id) && CronOccurrences.TryAdd(occurrence.Id, occurrence))
                        inserted++;
                    else
                        CronOccurrenceIndex.TryRemove(indexKey, out _);
                }
                return inserted;
            });

            return Task.FromResult(count);
        }

        public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            return Task.FromResult(WriteGraph(() =>
            {
                if (!IsStructuralMutationActivationState()) return 0;
                var count = 0;
                var now = _clock.UtcNow;
                foreach (var id in cronTickerOccurrences)
                {
                    if (!CronOccurrences.TryGetValue(id, out var current)
                        || current.Status == TickerStatus.InProgress
                        || current.AcquisitionToken.HasValue
                        || current.LeaseUntil.HasValue && current.LeaseUntil.Value > now)
                        continue;

                    if (CronOccurrences.TryRemove(new KeyValuePair<Guid, CronTickerOccurrenceEntity<TCronTicker>>(id, current)))
                    {
                        CronOccurrenceIndex.TryRemove((current.ExecutionTime, current.CronTickerId), out _);
                        CronOccurrenceResults.TryRemove(id, out _);
                        count++;
                    }
                }
                return count;
            }));
        }

        #region Retention

        public Task<RetentionChainBatchResult> DeleteEligibleTimeTickerChainsAsync(
            RetentionCutoffs cutoffs, int batchSize, RetentionCursor cursor, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0 || cutoffs is null || !cutoffs.HasAny)
                return Task.FromResult(RetentionChainBatchResult.Empty);

            var now = _clock.UtcNow;

            // Hold the write lock for the whole examine-and-delete so each chain is judged and removed
            // atomically with respect to structural mutations, and eligibility is (re)read from the current
            // node values at the moment of deletion.
            var result = WriteGraph(() =>
            {
                if (!IsRunnableActivationState())
                    return RetentionChainBatchResult.Empty;
                // Candidate ROOTS: no parent, root node itself eligible (a necessary condition for the whole
                // chain), strictly after the keyset cursor, ordered by (ExecutedAt, Id). Bounded by Take:
                // deletion work never exceeds batchSize chains, and one extra candidate detects HasMore.
                var candidates = TimeTickers.Values
                    .Where(t => t.ParentId == null
                                && t.ExecutedAt.HasValue
                                && IsTimeNodeEligibleForRetention(t, cutoffs, now)
                                && cursor.IsBefore(t.ExecutedAt.Value, t.Id))
                    .OrderBy(t => t.ExecutedAt!.Value)
                    .ThenBy(t => t.Id)
                    .Take(batchSize + 1)
                    .ToList();

                var hasMore = candidates.Count > batchSize;
                var examineCount = Math.Min(candidates.Count, batchSize);

                var deletedRows = 0;
                var nextCursor = RetentionCursor.Start; // wrap by default (end of traversal)

                for (var i = 0; i < examineCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var root = candidates[i];

                    // Advance the cursor past every examined root — deleted OR retained — so a blocked chain
                    // is never reselected on the next call and cannot starve later eligible chains.
                    if (hasMore)
                        nextCursor = RetentionCursor.After(root.ExecutedAt!.Value, root.Id);

                    var subtree = new List<TTimeTicker>();
                    if (!TryCollectEligibleSubtree(
                            root.Id, cutoffs, now, cutoffs.MaxNodesPerChain, subtree, cancellationToken))
                        continue; // any node ineligible → retain the whole chain, but the cursor still advanced

                    foreach (var node in subtree)
                    {
                        if (TimeTickers.TryRemove(node.Id, out var removed) && removed.ParentId.HasValue)
                            RemoveChildIndex(removed.ParentId.Value, removed.Id);
                        TimeTickerResults.TryRemove(node.Id, out _);
                        deletedRows++;
                    }
                }

                return new RetentionChainBatchResult(deletedRows, hasMore, nextCursor);
            });

            return Task.FromResult(result);
        }

        // Collect a whole subtree with an EXPLICIT stack (never recursion) so an arbitrarily deep
        // chain cannot overflow the call stack. Returns false as soon as ANY node is ineligible,
        // missing, or the bound is exceeded so the caller retains the entire chain intact. A visited
        // set guards against cycles / DAG re-entry (each node counted and collected at most once).
        // Node values are read fresh from the store (recheck at delete time).
        private bool TryCollectEligibleSubtree(
            Guid rootId, RetentionCutoffs cutoffs, DateTime now, int maxNodes,
            List<TTimeTicker> collected, CancellationToken cancellationToken)
        {
            var stack = new Stack<Guid>();
            var visited = new HashSet<Guid>();
            stack.Push(rootId);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nodeId = stack.Pop();
                if (!visited.Add(nodeId))
                    continue; // already reached via another edge — do not re-collect or double count

                if (collected.Count >= maxNodes)
                    return false; // bound exceeded → fail closed, retain whole chain

                if (!TimeTickers.TryGetValue(nodeId, out var node))
                    return false; // cannot verify → retain

                if (!IsTimeNodeEligibleForRetention(node, cutoffs, now))
                    return false;

                collected.Add(node);

                foreach (var childId in GetChildrenIds(nodeId))
                    stack.Push(childId);
            }

            return true;
        }

        private static bool IsTimeNodeEligibleForRetention(TTimeTicker node, RetentionCutoffs cutoffs, DateTime now)
        {
            var cutoff = cutoffs.ForStatus(node.Status); // null when non-terminal or window unset
            if (cutoff is null)
                return false;
            if (node.ExecutedAt is null || node.ExecutedAt.Value >= cutoff.Value)
                return false;
            return IsNotActivelyOwned(node.AcquisitionToken, node.LeaseUntil, now);
        }

        public Task<RetentionBatchResult> DeleteEligibleCronTickerOccurrencesAsync(
            RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0 || cutoffs is null || !cutoffs.HasAny)
                return Task.FromResult(RetentionBatchResult.Empty);

            var result = WriteGraph(() =>
            {
                if (!IsRunnableActivationState())
                    return RetentionBatchResult.Empty;
                var now = _clock.UtcNow;
                var deleted = 0;
                var hasMore = false;

                foreach (var occurrence in CronOccurrences.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsCronOccurrenceEligibleForRetention(occurrence, cutoffs, now))
                        continue;

                    if (deleted >= batchSize)
                    {
                        hasMore = true;
                        break;
                    }

                    // Remove exactly the value that was rechecked. Even callers that do not
                    // participate in GraphLock cannot cause a newer replacement to be removed.
                    if (CronOccurrences.TryGetValue(occurrence.Id, out var current)
                        && IsCronOccurrenceEligibleForRetention(current, cutoffs, now)
                        && ((ICollection<KeyValuePair<Guid, CronTickerOccurrenceEntity<TCronTicker>>>)CronOccurrences)
                            .Remove(new KeyValuePair<Guid, CronTickerOccurrenceEntity<TCronTicker>>(
                                occurrence.Id, current)))
                    {
                        CronOccurrenceIndex.TryRemove((current.ExecutionTime, current.CronTickerId), out _);
                        CronOccurrenceResults.TryRemove(occurrence.Id, out _);
                        deleted++;
                    }
                }

                return new RetentionBatchResult(deleted, hasMore);
            });

            return Task.FromResult(result);
        }

        private static bool IsCronOccurrenceEligibleForRetention(
            CronTickerOccurrenceEntity<TCronTicker> occurrence, RetentionCutoffs cutoffs, DateTime now)
        {
            var cutoff = cutoffs.ForStatus(occurrence.Status);
            if (cutoff is null)
                return false;
            if (occurrence.ExecutedAt is null || occurrence.ExecutedAt.Value >= cutoff.Value)
                return false;
            return IsNotActivelyOwned(occurrence.AcquisitionToken, occurrence.LeaseUntil, now);
        }

        // Guards against deleting actively-owned work. A terminal write clears AcquisitionToken (the
        // authoritative live-generation marker) but deliberately leaves LockHolder and the last-renewed
        // LeaseUntil in place — so LockHolder on a terminal row is stale bookkeeping, not an active claim,
        // and requiring it to be null would make retention delete nothing. The real signals are: no live
        // generation (AcquisitionToken == null) and no still-live lease (LeaseUntil in the past or absent).
        private static bool IsNotActivelyOwned(Guid? acquisitionToken, DateTime? leaseUntil, DateTime now)
            => !acquisitionToken.HasValue
               && (!leaseUntil.HasValue || leaseUntil.Value <= now);

        #endregion

        public Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0)
                return Task.FromResult(Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>());

            var currentAtMutationBoundary = occurrenceIds.Where(id =>
                    CronOccurrences.TryGetValue(id, out var occurrence)
                    && CanAcquireCronOccurrence(occurrence)
                    && IsCurrentCronOccurrence(occurrence))
                .ToHashSet();
            foreach (var id in currentAtMutationBoundary)
                BeforeCronOccurrenceMutationLockHook?.Invoke(id);

            return Task.FromResult(WriteGraph(() =>
            {
                var now = _clock.UtcNow;
                var acquired = new List<CronTickerOccurrenceEntity<TCronTicker>>();
                foreach (var id in occurrenceIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CronOccurrences.TryGetValue(id, out var occurrence))
                        continue;

                    if (!CanAcquireCronOccurrence(occurrence))
                        continue;
                    if (!IsCurrentCronOccurrence(occurrence))
                    {
                        if (!currentAtMutationBoundary.Contains(id))
                            QuarantineStaleCronOccurrence(occurrence, now);
                        continue;
                    }

                    var updated = CloneCronOccurrence(occurrence);
                    updated.LockHolder = _lockHolder;
                    updated.LockedAt = now;
                    updated.LeaseUntil = now.Add(_leaseDuration);
                    updated.AcquisitionToken = Guid.NewGuid();
                    updated.Status = TickerStatus.InProgress;
                    updated.UpdatedAt = now;

                    if (TryUpdateCronOccurrence(id, updated, occurrence))
                        acquired.Add(updated);
                }
                return acquired.ToArray();
            }));
        }

        public Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
        {
            if (staleThreshold <= TimeSpan.Zero)
                return Task.FromResult(0);

            var now = _clock.UtcNow;
            var cutoff = now - staleThreshold;
            var count = 0;

            var staleOccurrences = CronOccurrences.Values
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime < cutoff)
                .ToArray();

            foreach (var occurrence in staleOccurrences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!CronOccurrences.TryGetValue(occurrence.Id, out var current))
                    continue;

                var updated = CloneCronOccurrence(current);
                updated.Status = TickerStatus.Skipped;
                updated.SkippedReason = "Missed: occurrence was pending when the application restarted";
                updated.UpdatedAt = now;

                if (TryUpdateCronOccurrence(occurrence.Id, updated, current))
                    count++;
            }

            return Task.FromResult(count);
        }

        #endregion

        #region Helper Methods

        private bool IsCurrentCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence)
            => CronTickers.TryGetValue(occurrence.CronTickerId, out var definition)
               && occurrence.DefinitionRevision == definition.DefinitionRevision;

        private void QuarantineStaleCronOccurrence(
            CronTickerOccurrenceEntity<TCronTicker> occurrence, DateTime now)
        {
            if (!CronOccurrences.TryGetValue(occurrence.Id, out var current) ||
                current.Status is not (TickerStatus.Idle or TickerStatus.Queued))
                return;

            // A live owner is evidence of an in-flight old generation. Revision fencing rejects its
            // queued-to-running transition, but quarantine waits until lease/lock recovery makes mutation safe.
            if (current.LeaseUntil > now ||
                (!string.IsNullOrEmpty(current.LockHolder) && current.AcquisitionToken.HasValue &&
                 !current.LeaseUntil.HasValue))
                return;

            var quarantined = CloneCronOccurrence(current);
            quarantined.Status = TickerStatus.Skipped;
            quarantined.SkippedReason = "Quarantined because its Cron definition revision is stale.";
            quarantined.ExecutedAt ??= now;
            quarantined.LockHolder = null;
            quarantined.LockedAt = null;
            quarantined.LeaseUntil = null;
            quarantined.AcquisitionToken = null;
            quarantined.UpdatedAt = now;
            TryUpdateCronOccurrence(current.Id, quarantined, current, requireCurrentDefinition: false);
        }

        private TTimeTicker BuildTickerHierarchy(TTimeTicker ticker)
        {
            var root = CloneTicker(ticker);
            root.Children = BuildChildrenHierarchy(ticker.Id);
            return root;
        }

        private List<TTimeTicker> BuildChildrenHierarchy(Guid parentId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var children) || children.IsEmpty)
                return new List<TTimeTicker>();

            var results = new List<TTimeTicker>(children.Count);

            foreach (var childId in children.Keys)
            {
                if (!TimeTickers.TryGetValue(childId, out var child))
                    continue;

                var clonedChild = CloneTicker(child);
                clonedChild.Children = BuildChildrenHierarchy(child.Id);
                results.Add(clonedChild);
            }

            return results;
        }

        // Matches EF Core's MappingExtensions.ForQueueTimeTickers but uses an in-memory
        // children index. Walks the chain recursively to unbounded depth (matching the
        // probe-and-extend behavior in the EF provider) so deep chains aren't truncated.
        private TimeTickerEntity ForQueueTimeTickers(TTimeTicker ticker)
        {
            var root = new TimeTickerEntity
            {
                Id = ticker.Id,
                Function = ticker.Function,
                RequestContractVersion = ticker.RequestContractVersion,
                RequestContractFingerprint = ticker.RequestContractFingerprint,
                Retries = ticker.Retries,
                RetryIntervals = ticker.RetryIntervals,
                TimeoutSeconds = ticker.TimeoutSeconds,
                UpdatedAt = ticker.UpdatedAt,
                ParentId = ticker.ParentId,
                ExecutionTime = ticker.ExecutionTime,
                AcquisitionToken = ticker.AcquisitionToken,
                ChainRootId = ticker.Id,
                ChainGeneration = ticker.ChainGeneration,
                Children = BuildQueueDescendants(ticker.Id, ticker.Id, ticker.ChainGeneration),
            };

            return root;
        }

        // Recursive descendants walker for the queue projection. Mirrors the EF
        // provider's probe-and-extend semantics: unbounded depth, only includes
        // chain children (ExecutionTime == null) for the direct-children layer.
        private List<TimeTickerEntity> BuildQueueDescendants(
            Guid parentId, Guid chainRootId, Guid? chainGeneration)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var directChildren) || directChildren.IsEmpty)
                return new List<TimeTickerEntity>();

            var children = new List<TimeTickerEntity>(directChildren.Count);
            foreach (var childId in directChildren.Keys)
            {
                if (!TimeTickers.TryGetValue(childId, out var ch))
                    continue;

                // Only chain children with null ExecutionTime, matching the EF
                // .Include(x => x.Children.Where(y => y.ExecutionTime == null)) filter
                // on the direct-children layer.
                if (ch.ExecutionTime != null)
                    continue;

                children.Add(new TimeTickerEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    RequestContractVersion = ch.RequestContractVersion,
                    RequestContractFingerprint = ch.RequestContractFingerprint,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    TimeoutSeconds = ch.TimeoutSeconds,
                    RunCondition = ch.RunCondition,
                    ParentId = ch.ParentId,
                    ChainRootId = chainRootId,
                    ChainGeneration = chainGeneration,
                    Children = BuildQueueDescendantsAtAnyDepth(ch.Id, chainRootId, chainGeneration),
                });
            }

            return children;
        }

        // Same as BuildQueueDescendants but without the ExecutionTime filter — once
        // we're past the direct-children layer, every descendant is a chain node.
        private List<TimeTickerEntity> BuildQueueDescendantsAtAnyDepth(
            Guid parentId, Guid chainRootId, Guid? chainGeneration)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var directChildren) || directChildren.IsEmpty)
                return new List<TimeTickerEntity>();

            var children = new List<TimeTickerEntity>(directChildren.Count);
            foreach (var childId in directChildren.Keys)
            {
                if (!TimeTickers.TryGetValue(childId, out var ch))
                    continue;

                children.Add(new TimeTickerEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    RequestContractVersion = ch.RequestContractVersion,
                    RequestContractFingerprint = ch.RequestContractFingerprint,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    TimeoutSeconds = ch.TimeoutSeconds,
                    RunCondition = ch.RunCondition,
                    ParentId = ch.ParentId,
                    ChainRootId = chainRootId,
                    ChainGeneration = chainGeneration,
                    Children = BuildQueueDescendantsAtAnyDepth(ch.Id, chainRootId, chainGeneration),
                });
            }
            return children;
        }

        private void AddChildIndex(Guid parentId, Guid childId)
        {
            var children = ChildrenIndex.GetOrAdd(parentId, _ => new ConcurrentDictionary<Guid, byte>());
            children.TryAdd(childId, 0);
        }

        private void RemoveChildIndex(Guid parentId, Guid childId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var children))
                return;

            children.TryRemove(childId, out _);

            // Optional: cleanup empty buckets
            if (children.IsEmpty)
            {
                ChildrenIndex.TryRemove(parentId, out _);
            }
        }

        private Guid[] GetChildrenIds(Guid parentId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var children))
                return Array.Empty<Guid>();

            return children.Keys.ToArray();
        }

        private static bool IsFencedTerminalWrite(InternalFunctionContext functionContext)
            => functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ReleaseLock)) ||
               (functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                    or TickerStatus.Cancelled or TickerStatus.Skipped);

        private bool CanAcquire(TTimeTicker ticker)
        {
            // Match EF provider logic: WhereCanAcquire
            // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
            return ((ticker.Status == TickerStatus.Idle || ticker.Status == TickerStatus.Queued) && ticker.LockHolder == _lockHolder) ||
                   ((ticker.Status == TickerStatus.Idle || ticker.Status == TickerStatus.Queued) && ticker.LockedAt == null);
        }
        
        private bool CanAcquireCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence)
        {
            // Match EF provider logic: WhereCanAcquire
            // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
            return ((occurrence.Status == TickerStatus.Idle || occurrence.Status == TickerStatus.Queued) && occurrence.LockHolder == _lockHolder) ||
                   ((occurrence.Status == TickerStatus.Idle || occurrence.Status == TickerStatus.Queued) && occurrence.LockedAt == null);
        }

        private bool CanReleaseOwned(TTimeTicker ticker)
            => ticker.Status is TickerStatus.Idle or TickerStatus.Queued &&
               ticker.LockHolder == _lockHolder &&
               ticker.AcquisitionToken.HasValue;

        private bool CanReleaseOwned(CronTickerOccurrenceEntity<TCronTicker> occurrence)
            => occurrence.Status is TickerStatus.Idle or TickerStatus.Queued &&
               occurrence.LockHolder == _lockHolder &&
               occurrence.AcquisitionToken.HasValue;

        private TTimeTicker CloneTicker(TTimeTicker ticker)
        {
            var cloned = new TTimeTicker
            {
                Id = ticker.Id,
                Function = ticker.Function,
                RequestContractVersion = ticker.RequestContractVersion,
                RequestContractFingerprint = ticker.RequestContractFingerprint,
                Status = ticker.Status,
                Retries = ticker.Retries,
                RetryCount = ticker.RetryCount,
                ExecutionTime = ticker.ExecutionTime,
                InitIdentifier = ticker.InitIdentifier,
                LockHolder = ticker.LockHolder,
                LockedAt = ticker.LockedAt,
                ParentId = ticker.ParentId,
                Request = ticker.Request,
                ExceptionMessage = ticker.ExceptionMessage,
                SkippedReason = ticker.SkippedReason,
                ElapsedTime = ticker.ElapsedTime,
                RetryIntervals = ticker.RetryIntervals,
                RunCondition = ticker.RunCondition,
                ExecutedAt = ticker.ExecutedAt,
                CreatedAt = ticker.CreatedAt,
                UpdatedAt = ticker.UpdatedAt,
                Description = ticker.Description,
                LeaseUntil = ticker.LeaseUntil,
                AcquisitionToken = ticker.AcquisitionToken,
                ChainRootId = ticker.ChainRootId,
                ChainGeneration = ticker.ChainGeneration,
                OnStale = ticker.OnStale,
                StaleRestartCount = ticker.StaleRestartCount,
                TimeoutSeconds = ticker.TimeoutSeconds,
                Children = new List<TTimeTicker>()
            };
            
            return cloned;
        }
        
        private CronTickerOccurrenceEntity<TCronTicker> CloneCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence)
        {
            return new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = occurrence.Id,
                CronTicker = occurrence.CronTicker,
                CronTickerId = occurrence.CronTickerId,
                DefinitionRevision = occurrence.DefinitionRevision,
                Status = occurrence.Status,
                RetryCount = occurrence.RetryCount,
                ExecutionTime = occurrence.ExecutionTime,
                LockHolder = occurrence.LockHolder,
                LockedAt = occurrence.LockedAt,
                ExceptionMessage = occurrence.ExceptionMessage,
                SkippedReason = occurrence.SkippedReason,
                ElapsedTime = occurrence.ElapsedTime,
                ExecutedAt = occurrence.ExecutedAt,
                CreatedAt = occurrence.CreatedAt,
                UpdatedAt = occurrence.UpdatedAt,
                LeaseUntil = occurrence.LeaseUntil,
                AcquisitionToken = occurrence.AcquisitionToken,
                StaleRestartCount = occurrence.StaleRestartCount
            };
        }


        private void ApplyFunctionContextToTicker(TTimeTicker ticker, InternalFunctionContext context)
        {
            var propsToUpdate = context.GetPropsToUpdate();

            // STATUS / SKIPPED
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                context.Status != TickerStatus.Skipped)
            {
                ticker.Status = context.Status;
            }
            else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
            {
                ticker.Status = context.Status;
                ticker.SkippedReason = context.ExceptionDetails;
            }

            // EXECUTED_AT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                ticker.ExecutedAt = context.ExecutedAt;
            }

            // EXCEPTION DETAILS
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                context.Status != TickerStatus.Skipped)
            {
                ticker.ExceptionMessage = context.ExceptionDetails;
            }

            // ELAPSED_TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                ticker.ElapsedTime = context.ElapsedTime;
            }

            // RETRY COUNT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                ticker.RetryCount = context.RetryCount;
            }

            // RELEASE LOCK
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                ticker.LockHolder = null;
                ticker.LockedAt = null;
                ticker.LeaseUntil = null;
                ticker.AcquisitionToken = null;
                if (ticker.ParentId == null)
                    ticker.ChainGeneration = null;
            }

            if (IsFencedTerminalWrite(context))
            {
                ticker.LeaseUntil = null;
                ticker.AcquisitionToken = null;
            }

            // UPDATED_AT ALWAYS
            ticker.UpdatedAt = _clock.UtcNow;
        }
        
        private void ApplyFunctionContextToCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence, InternalFunctionContext context)
        {
            var propsToUpdate = context.GetPropsToUpdate();

            // STATUS / SKIPPED
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                context.Status != TickerStatus.Skipped)
            {
                occurrence.Status = context.Status;
            }
            else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
            {
                occurrence.Status = context.Status;
                occurrence.SkippedReason = context.ExceptionDetails;
            }

            // EXECUTED_AT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                occurrence.ExecutedAt = context.ExecutedAt;
            }

            // EXCEPTION DETAILS
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                context.Status != TickerStatus.Skipped)
            {
                occurrence.ExceptionMessage = context.ExceptionDetails;
            }

            // ELAPSED_TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                occurrence.ElapsedTime = context.ElapsedTime;
            }

            // RETRY COUNT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                occurrence.RetryCount = context.RetryCount;
            }

            // RELEASE LOCK
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                occurrence.LockHolder = null;
                occurrence.LockedAt = null;
            }

            if (IsFencedTerminalWrite(context))
            {
                occurrence.LeaseUntil = null;
                occurrence.AcquisitionToken = null;
            }

            // UPDATED_AT ALWAYS
            occurrence.UpdatedAt = _clock.UtcNow;
        }

        #endregion

        #region Queryable

        public ITickerQueryable<TTimeTicker> TimeTickersQuery()
        {
            return new InMemoryTickerQueryable<TTimeTicker>(_ =>
                Task.FromResult(ReadGraph(() => TimeTickers.Values.ToList())));
        }

        public ITickerQueryable<TCronTicker> CronTickersQuery()
        {
            return new InMemoryTickerQueryable<TCronTicker>(_ =>
                Task.FromResult(ReadGraph(() => CronTickers.Values.ToList())));
        }

        public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
        {
            return new InMemoryTickerQueryable<CronTickerOccurrenceEntity<TCronTicker>>(_ =>
                Task.FromResult(ReadGraph(() => CronOccurrences.Values.ToList())));
        }

        #endregion
    }
}
