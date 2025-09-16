using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    public class InternalFunctionContext
    {
        private HashSet<string> ParametersToUpdate { get; set; } = [];
        public string FunctionName { get; set; }
        public Guid TickerId { get; set; }
        public Guid CronTickerId { get; set; }
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

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(InternalFunctionContext))]
        public InternalFunctionContext SetProperty<T>(Expression<Func<InternalFunctionContext, T>> property, T value)
        {
            ParametersToUpdate ??= [];
            
            if (property.Body is MemberExpression { Member: PropertyInfo prop })
            {
                prop.SetValue(this, value);
                ParametersToUpdate.Add(prop.Name);
            }
            else
                throw new ArgumentException("Expression must point to a property", nameof(property));

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
