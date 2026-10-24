using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using TickerQ.Utilities.Infrastructure;
using System.Reflection;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    public class InternalFunctionContext
    {
        // Compiled setter cache to avoid reflection on every SetProperty call
        private static readonly ConcurrentDictionary<string, (Action<InternalFunctionContext, object> Setter, string Name)> SetterCache = new();

        public HashSet<string> ParametersToUpdate { get; set; } = [];
        // Cached function delegate and priority for performance optimization
        // Eliminates dictionary lookups during execution
        [JsonIgnore]
        public TickerFunctionDelegate CachedDelegate { get; set; }
        public TickerTaskPriority CachedPriority { get; set; }
        public int CachedMaxConcurrency { get; set; }
        public string FunctionName { get; set; }
        /// <summary>
        /// Immutable physical runtime partition carried with this execution. Runtime-produced
        /// contexts always set this value; namespace-less compatibility contexts are admitted only
        /// by an explicitly LegacyGlobal queue-only runtime.
        /// </summary>
        public string RuntimePartitionKey { get; set; }
        public int? RequestContractVersion { get; set; }
        public string RequestContractFingerprint { get; set; }
        public Guid TickerId { get; set; }
        public Guid? ParentId { get; set; }
        /// <summary>
        /// Root aggregate that durably owns this execution. Equal to <see cref="TickerId"/>
        /// for roots and propagated unchanged to every chain descendant. Execution-only;
        /// providers such as Redis use it to update children embedded in the root document.
        /// </summary>
        [JsonIgnore]
        public Guid? ChainRootId { get; set; }
        /// <summary>Authoritative generation of <see cref="ChainRootId"/> for this execution.</summary>
        [JsonIgnore]
        public Guid? ChainGeneration { get; set; }
        public TickerType Type { get; set; }
        public int Retries { get; set; }
        public int RetryCount { get; set; }
        public TickerStatus Status { get; set; }
        public long ElapsedTime { get; set; }
        public string ExceptionDetails { get; set; }
        public DateTime ExecutedAt { get; set; }
        public int[] RetryIntervals { get; set; }
        public bool ReleaseLock { get; set; }
        public DateTime ExecutionTime { get; set; }
        public MissedExecutionAction MissedExecutionAction { get; set; }
        public TimeSpan? MaximumLateness { get; set; }
        public double? ProgressPercent { get; set; }
        public string ProgressMessage { get; set; }
        public DateTime? ProgressUpdatedAt { get; set; }
        /// <summary>
        /// Per-attempt execution timeout carried from the ticker row (cron template
        /// for occurrences). Null inherits the global default; &lt;= 0 disables.
        /// </summary>
        public int? TimeoutSeconds { get; set; }
        public bool SkipIfAlreadyRunning { get; set; }
        /// <summary>
        /// Current InProgress generation of the root row this context executes under. Set at
        /// acquisition (minted fresh on every transition to InProgress) and carried through
        /// execution so the terminal write and lease renewal can be fenced on it. Null for
        /// chain children (they run under their root's lease; <see cref="ChainGeneration"/>
        /// fences their writes).
        /// </summary>
        public Guid? AcquisitionToken { get; set; }
        public RunCondition RunCondition { get; set; }
        /// <summary>
        /// Optional result published by a successful terminal execution, carried to the persistence
        /// write so it commits atomically with the terminal status. Execution-only: it is never part
        /// of the persisted wire row itself and never serialized on this context. Set only via the
        /// success path so failed/retried/cancelled/skipped attempts never publish a result.
        /// </summary>
        [JsonIgnore]
        public TickerResultEnvelope ResultEnvelope { get; set; }
        /// <summary>
        /// Direct parent's already-validated committed result supplied by an execution transport.
        /// When present, the runtime consumes this snapshot instead of querying local persistence.
        /// Execution-only and never serialized as part of the function context.
        /// </summary>
        [JsonIgnore]
        public TickerResultEnvelope ParentResultEnvelope { get; set; }
        /// <summary>Execution-only request override used by ephemeral dashboard test runs.</summary>
        [JsonIgnore]
        public byte[] RequestPayload { get; set; }
        /// <summary>Execution-only marker exposed to the function body.</summary>
        [JsonIgnore]
        public bool IsTestRun { get; set; }
        /// <summary>
        /// Execution-only marker for an already-completed ancestor traversed while retrying a later
        /// chain step. The runtime follows its single retry path without invoking the function again.
        /// </summary>
        [JsonIgnore]
        public bool IsChainRetryPassThrough { get; set; }
        /// <summary>
        /// Execution-only marker for a completed grouped branch whose recorded outcome participates
        /// in the group's recalculated result without invoking that branch again.
        /// </summary>
        [JsonIgnore]
        public bool IsChainRetryReusedTerminal { get; set; }
        /// <summary>
        /// Execution-only marker identifying the persisted root that owns an in-place retry.
        /// This is true whether the selected retry point is the root itself or a descendant.
        /// </summary>
        [JsonIgnore]
        public bool IsChainRetryRoot { get; set; }
        /// <summary>
        /// Execution-only marker for a completed root function acting as the coordinator while a
        /// descendant path is retried. The root function itself is not invoked again.
        /// </summary>
        [JsonIgnore]
        public bool IsChainRetryCoordinator { get; set; }
        /// <summary>The coordinator root's terminal outcome before it was acquired for retry.</summary>
        [JsonIgnore]
        public TickerStatus? ChainRetryPreviousStatus { get; set; }
        public List<InternalFunctionContext> TimeTickerChildren { get; set; } = [];

        // Runtime updates avoid constructing expression trees and boxing scalar values.
        // Tracking is intentional even when the value is unchanged: a null result, for
        // example, must still clear a previously committed result on a successful rerun.
        internal InternalFunctionContext SetStatus(TickerStatus value)
        {
            Status = value;
            return TrackProperty(nameof(Status));
        }

        internal InternalFunctionContext SetExecutedAt(DateTime value)
        {
            ExecutedAt = value;
            return TrackProperty(nameof(ExecutedAt));
        }

        internal InternalFunctionContext SetElapsedTime(long value)
        {
            ElapsedTime = value;
            return TrackProperty(nameof(ElapsedTime));
        }

        internal InternalFunctionContext SetExceptionDetails(string value)
        {
            ExceptionDetails = value;
            return TrackProperty(nameof(ExceptionDetails));
        }

        internal InternalFunctionContext SetResultEnvelope(TickerResultEnvelope value)
        {
            ResultEnvelope = value;
            return TrackProperty(nameof(ResultEnvelope));
        }

        internal InternalFunctionContext SetRetryCount(int value)
        {
            RetryCount = value;
            return TrackProperty(nameof(RetryCount));
        }

        internal InternalFunctionContext SetReleaseLock(bool value)
        {
            ReleaseLock = value;
            return TrackProperty(nameof(ReleaseLock));
        }

        internal InternalFunctionContext SetExecutionTime(DateTime value)
        {
            ExecutionTime = value;
            return TrackProperty(nameof(ExecutionTime));
        }

        internal InternalFunctionContext SetProgress(double percent, string message, DateTime updatedAt)
        {
            ProgressPercent = percent;
            ProgressMessage = message;
            ProgressUpdatedAt = updatedAt;
            TrackProperty(nameof(ProgressPercent));
            TrackProperty(nameof(ProgressMessage));
            return TrackProperty(nameof(ProgressUpdatedAt));
        }

        private InternalFunctionContext TrackProperty(string name)
        {
            (ParametersToUpdate ??= []).Add(name);
            return this;
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(InternalFunctionContext))]
        public InternalFunctionContext SetProperty<T>(Expression<Func<InternalFunctionContext, T>> property, T value)
        {
            ParametersToUpdate ??= [];

            if (property.Body is not MemberExpression { Member: PropertyInfo prop })
                throw new ArgumentException("Expression must point to a property", nameof(property));

            var cached = SetterCache.GetOrAdd(prop.Name, _ =>
            {
                var instance = Expression.Parameter(typeof(InternalFunctionContext), "obj");
                var val = Expression.Parameter(typeof(object), "val");
                var assign = Expression.Assign(
                    Expression.Property(instance, prop),
                    Expression.Convert(val, prop.PropertyType));
                var lambda = Expression.Lambda<Action<InternalFunctionContext, object>>(assign, instance, val);
                return (lambda.CompileAotSafe(), prop.Name);
            });

            cached.Setter(this, value);
            ParametersToUpdate.Add(cached.Name);

            return this;
        }

        public InternalFunctionContext ResetUpdateProps()
        {
             ParametersToUpdate.Clear();
             return this;
        }

        public HashSet<string> GetPropsToUpdate()
            => ParametersToUpdate;
    }
}
