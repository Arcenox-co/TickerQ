using System.Collections.Frozen;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// One immutable, self-consistent snapshot of the function registry: the frozen execution
    /// functions, the canonical wire descriptors, and the internal runtime request metadata. The
    /// three tables are captured together and published as a single unit (one <c>Volatile.Write</c>),
    /// so a reader that captures the snapshot sees a coherent view that never changes underneath it.
    /// </summary>
    internal sealed class TickerFunctionRegistrySnapshot
    {
        public static readonly TickerFunctionRegistrySnapshot Empty = new(
            FrozenDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>.Empty,
            FrozenDictionary<string, TickerFunctionDescriptor>.Empty,
            FrozenDictionary<string, TickerRuntimeRequestMetadata>.Empty);

        public TickerFunctionRegistrySnapshot(
            FrozenDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> functions,
            FrozenDictionary<string, TickerFunctionDescriptor> descriptors,
            FrozenDictionary<string, TickerRuntimeRequestMetadata> runtimeRequests)
        {
            Functions = functions;
            Descriptors = descriptors;
            RuntimeRequests = runtimeRequests;
        }

        public FrozenDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> Functions { get; }

        public FrozenDictionary<string, TickerFunctionDescriptor> Descriptors { get; }

        public FrozenDictionary<string, TickerRuntimeRequestMetadata> RuntimeRequests { get; }
    }
}
