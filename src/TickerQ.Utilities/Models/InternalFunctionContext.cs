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
        public string FunctionName { get; set; }
        public Guid TickerId { get; set; }
        public Guid? ParentId { get; set; }
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
        public RunCondition RunCondition { get; set; }
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
