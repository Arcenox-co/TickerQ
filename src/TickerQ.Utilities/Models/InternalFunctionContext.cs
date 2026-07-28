using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
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
        /// <summary>
        /// Per-attempt execution timeout carried from the ticker row (cron template
        /// for occurrences). Null inherits the global default; &lt;= 0 disables.
        /// </summary>
        public int? TimeoutSeconds { get; set; }
        /// <summary>
        /// Current InProgress generation of the root row this context executes under. Set at
        /// acquisition (minted fresh on every transition to InProgress) and carried through
        /// execution so the terminal write and lease renewal can be fenced on it. Null for
        /// chain children (they run under their root's lock and carry no generation).
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
        public List<InternalFunctionContext> TimeTickerChildren { get; set; } = [];

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
                return (lambda.Compile(), prop.Name);
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
