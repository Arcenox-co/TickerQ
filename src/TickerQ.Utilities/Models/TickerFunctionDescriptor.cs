using System;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Canonical, immutable descriptor for a ticker function's request contract and its wire
    /// scheduling metadata. One descriptor is shared by every registration path (source-generated
    /// attribute, interface-based, fluent, Hub, and SDK). A function without a request payload has
    /// <see cref="Request"/> == <c>null</c>; absence is never encoded as empty metadata.
    /// </summary>
    /// <remarks>
    /// This is a wire descriptor. It carries wire metadata only — <see cref="ContractVersion"/>,
    /// <see cref="FunctionName"/>, <see cref="Priority"/>, <see cref="CronExpression"/>, and
    /// <see cref="Request"/>. It intentionally excludes the delegate, max-concurrency, CLR
    /// <see cref="Type"/>, and <c>JsonTypeInfo</c>; that runtime metadata is tracked separately by
    /// <see cref="TickerQ.Utilities.TickerFunctionProvider"/>.
    /// </remarks>
    public sealed class TickerFunctionDescriptor : IEquatable<TickerFunctionDescriptor>
    {
        [JsonConstructor]
        public TickerFunctionDescriptor(
            string functionName,
            TickerTaskPriority priority = TickerTaskPriority.Normal,
            string cronExpression = null,
            int contractVersion = TickerRequestContractConstants.InitialContractVersion,
            TickerRequestContract request = null,
            TickerResultContract result = null)
        {
            if (functionName == null)
                throw new ArgumentNullException(nameof(functionName));
            if (string.IsNullOrWhiteSpace(functionName))
                throw new ArgumentException("Function name must be non-empty.", nameof(functionName));
            if (contractVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(contractVersion), contractVersion,
                    "Contract version must be positive.");

            FunctionName = functionName;
            Priority = priority;
            CronExpression = cronExpression;
            ContractVersion = contractVersion;
            Request = request?.WithContractVersion(contractVersion);
            Result = result?.WithContractVersion(contractVersion);
        }

        public string FunctionName { get; }

        public TickerTaskPriority Priority { get; }

        /// <summary>Final cron expression for the function, reconciled with the build output.</summary>
        public string CronExpression { get; }

        /// <summary>Contract version, starting at 1.</summary>
        public int ContractVersion { get; }

        /// <summary>Request contract, or null for request-less functions.</summary>
        public TickerRequestContract Request { get; }

        /// <summary>Declared optional result contract, or null for legacy/non-result functions.</summary>
        public TickerResultContract Result { get; }

        /// <summary>
        /// Returns a copy with reconciled scheduling metadata. Used by <c>Build()</c> to keep
        /// priority/cron coherent with the authoritative functions registry without forcing
        /// consumers to join independent dictionaries.
        /// </summary>
        internal TickerFunctionDescriptor WithSchedule(TickerTaskPriority priority, string cronExpression)
        {
            if (Priority == priority && string.Equals(CronExpression, cronExpression, StringComparison.Ordinal))
                return this;
            return new TickerFunctionDescriptor(FunctionName, priority, cronExpression, ContractVersion, Request, Result);
        }

        /// <summary>
        /// Contract-identity compatibility: same function name, contract version, and request shape.
        /// Excludes scheduling metadata (priority/cron), which is reconciled at build time and must
        /// not drive duplicate-registration conflicts.
        /// </summary>
        internal bool HasSameContract(TickerFunctionDescriptor other)
        {
            if (other is null) return false;
            return string.Equals(FunctionName, other.FunctionName, StringComparison.Ordinal)
                   && ContractVersion == other.ContractVersion
                   && Equals(Request, other.Request)
                   && Equals(Result, other.Result);
        }

        public bool Equals(TickerFunctionDescriptor other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(FunctionName, other.FunctionName, StringComparison.Ordinal)
                   && Priority == other.Priority
                   && string.Equals(CronExpression, other.CronExpression, StringComparison.Ordinal)
                   && ContractVersion == other.ContractVersion
                   && Equals(Request, other.Request)
                   && Equals(Result, other.Result);
        }

        public override bool Equals(object obj) => Equals(obj as TickerFunctionDescriptor);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(FunctionName, StringComparer.Ordinal);
            hash.Add(Priority);
            hash.Add(CronExpression, StringComparer.Ordinal);
            hash.Add(ContractVersion);
            hash.Add(Request);
            hash.Add(Result);
            return hash.ToHashCode();
        }

        public override string ToString()
            => $"{FunctionName} v{ContractVersion} priority={Priority} cron={CronExpression ?? "null"} request={(Request?.ToString() ?? "null")} result={(Result?.ToString() ?? "null")}";
    }
}
