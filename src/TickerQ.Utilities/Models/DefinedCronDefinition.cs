using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// The full definition of a seeded cron, captured from the cron chain builder after the same
    /// validation the cron manager applies. Seeds without a definition keep the legacy behavior and
    /// reconcile only the expression, contract identity, retries, retry intervals, and timeout.
    /// </summary>
    internal sealed class DefinedCronDefinition : IEquatable<DefinedCronDefinition>
    {
        internal string TimeZoneId { get; private init; }
        internal string SchedulePolicyName { get; private init; }
        internal long? SchedulePolicyVersion { get; private init; }
        internal BlockedOccurrenceBehavior? BlockedOccurrenceBehavior { get; private init; }
        internal MissedExecutionAction MissedExecutionAction { get; private init; }
        internal TimeSpan? MaximumLateness { get; private init; }
        internal CronCatchUpMode CatchUpMode { get; private init; }
        internal bool SkipIfAlreadyRunning { get; private init; }
        internal StaleAction OnStale { get; private init; }
        internal string Description { get; private init; }
        internal bool IsEnabled { get; private init; }
        internal byte[] Request { get; private init; }
        internal CronChainTemplate Chain { get; private init; }

        internal static DefinedCronDefinition From(CronTickerEntity cron) => new()
        {
            TimeZoneId = string.IsNullOrWhiteSpace(cron.TimeZoneId) ? null : cron.TimeZoneId,
            SchedulePolicyName = string.IsNullOrWhiteSpace(cron.SchedulePolicyName) ? null : cron.SchedulePolicyName,
            SchedulePolicyVersion = string.IsNullOrWhiteSpace(cron.SchedulePolicyName) ? null : cron.SchedulePolicyVersion,
            BlockedOccurrenceBehavior = string.IsNullOrWhiteSpace(cron.SchedulePolicyName) ? null : cron.BlockedOccurrenceBehavior,
            MissedExecutionAction = cron.MissedExecutionAction,
            MaximumLateness = cron.MaximumLateness,
            CatchUpMode = cron.CatchUpMode,
            SkipIfAlreadyRunning = cron.SkipIfAlreadyRunning,
            OnStale = cron.OnStale,
            Description = string.IsNullOrWhiteSpace(cron.Description) ? null : cron.Description,
            IsEnabled = cron.IsEnabled,
            Request = cron.Request is { Length: > 0 } ? cron.Request.ToArray() : Array.Empty<byte>(),
            Chain = NormalizeChain(cron.Chain),
        };

        internal DefinedCronDefinition Clone() => Copy();

        // A detached copy so manifests and persisted rows never share mutable request or chain instances.
        private DefinedCronDefinition Copy() => new()
        {
            TimeZoneId = TimeZoneId,
            SchedulePolicyName = SchedulePolicyName,
            SchedulePolicyVersion = SchedulePolicyVersion,
            BlockedOccurrenceBehavior = BlockedOccurrenceBehavior,
            MissedExecutionAction = MissedExecutionAction,
            MaximumLateness = MaximumLateness,
            CatchUpMode = CatchUpMode,
            SkipIfAlreadyRunning = SkipIfAlreadyRunning,
            OnStale = OnStale,
            Description = Description,
            IsEnabled = IsEnabled,
            Request = Request.ToArray(),
            Chain = CloneChain(Chain),
        };

        /// <summary>A chain with no children is a plain cron; store it as no chain.</summary>
        internal static CronChainTemplate NormalizeChain(CronChainTemplate chain)
            => chain is { Children.Count: > 0 } ? CloneChain(chain) : null;

        internal static CronChainTemplate CloneChain(CronChainTemplate chain)
        {
            if (chain == null) return null;
            var copy = new CronChainTemplate { DeleteBehavior = chain.DeleteBehavior };
            foreach (var child in chain.Children ?? new List<CronChainNode>())
                copy.Children.Add(CloneNode(child));
            return copy;
        }

        private static CronChainNode CloneNode(CronChainNode node)
        {
            var copy = new CronChainNode
            {
                Function = node.Function,
                Description = node.Description,
                Request = node.Request?.ToArray(),
                Retries = node.Retries,
                RetryIntervals = node.RetryIntervals?.ToArray(),
                RunCondition = node.RunCondition,
                OnStale = node.OnStale,
                TimeoutSeconds = node.TimeoutSeconds,
                RequestContractVersion = node.RequestContractVersion,
                RequestContractFingerprint = node.RequestContractFingerprint,
            };
            foreach (var child in node.Children ?? new List<CronChainNode>())
                copy.Children.Add(CloneNode(child));
            return copy;
        }

        internal static bool ChainEquals(CronChainTemplate left, CronChainTemplate right)
        {
            left = left is { Children.Count: > 0 } ? left : null;
            right = right is { Children.Count: > 0 } ? right : null;
            if (left == null || right == null) return left == null && right == null;
            return left.DeleteBehavior == right.DeleteBehavior && NodesEqual(left.Children, right.Children);
        }

        private static bool NodesEqual(IReadOnlyList<CronChainNode> left, IReadOnlyList<CronChainNode> right)
        {
            left ??= Array.Empty<CronChainNode>();
            right ??= Array.Empty<CronChainNode>();
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
            {
                var a = left[i];
                var b = right[i];
                if (!string.Equals(a.Function, b.Function, StringComparison.Ordinal)
                    || !string.Equals(a.Description, b.Description, StringComparison.Ordinal)
                    || !BytesEqual(a.Request, b.Request)
                    || a.Retries != b.Retries
                    || !IntsEqual(a.RetryIntervals, b.RetryIntervals)
                    || a.RunCondition != b.RunCondition
                    || a.OnStale != b.OnStale
                    || a.TimeoutSeconds != b.TimeoutSeconds
                    || a.RequestContractVersion != b.RequestContractVersion
                    || !string.Equals(a.RequestContractFingerprint, b.RequestContractFingerprint, StringComparison.Ordinal)
                    || !NodesEqual(a.Children, b.Children))
                    return false;
            }
            return true;
        }

        internal static bool BytesEqual(byte[] left, byte[] right)
            => (left ?? Array.Empty<byte>()).AsSpan().SequenceEqual(right ?? Array.Empty<byte>());

        internal static bool IntsEqual(int[] left, int[] right)
            => (left ?? Array.Empty<int>()).AsSpan().SequenceEqual(right ?? Array.Empty<int>());

        public bool Equals(DefinedCronDefinition other)
            => other != null
               && string.Equals(TimeZoneId, other.TimeZoneId, StringComparison.Ordinal)
               && string.Equals(SchedulePolicyName, other.SchedulePolicyName, StringComparison.Ordinal)
               && SchedulePolicyVersion == other.SchedulePolicyVersion
               && BlockedOccurrenceBehavior == other.BlockedOccurrenceBehavior
               && MissedExecutionAction == other.MissedExecutionAction
               && MaximumLateness == other.MaximumLateness
               && CatchUpMode == other.CatchUpMode
               && SkipIfAlreadyRunning == other.SkipIfAlreadyRunning
               && OnStale == other.OnStale
               && string.Equals(Description, other.Description, StringComparison.Ordinal)
               && IsEnabled == other.IsEnabled
               && BytesEqual(Request, other.Request)
               && ChainEquals(Chain, other.Chain);

        public override bool Equals(object obj) => obj is DefinedCronDefinition other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(TimeZoneId, SchedulePolicyName, MissedExecutionAction, CatchUpMode,
                SkipIfAlreadyRunning, Request?.Length ?? 0, Chain?.Children.Count ?? 0);
    }

    /// <summary>Result of applying a seed to a stored cron.</summary>
    internal readonly record struct DefinedCronSeedChange(bool Changed, bool DefinitionChanged);

    /// <summary>
    /// Applies a seed to a stored cron row. Shared by every persistence provider so seeded crons converge
    /// identically. A definition change bumps the cron's revision, which skips occurrences queued under the
    /// previous definition; a description or policy-revision change only updates the row.
    /// </summary>
    internal static class DefinedCronSeedApplier
    {
        internal static DefinedCronSeedChange Apply(CronTickerEntity cron, DefinedCronTickerSeed seed,
            bool applyContractIdentity)
        {
            var changed = false;
            var definitionChanged = false;

            void Definition(bool different, Action assign)
            {
                if (!different) return;
                assign();
                changed = true;
                definitionChanged = true;
            }

            void Metadata(bool different, Action assign)
            {
                if (!different) return;
                assign();
                changed = true;
            }

            Definition(!string.Equals(cron.Expression, seed.Expression, StringComparison.Ordinal),
                () => cron.Expression = seed.Expression);
            if (applyContractIdentity)
                Definition(!seed.MatchesIdentity(cron.RequestContractVersion, cron.RequestContractFingerprint), () =>
                {
                    cron.RequestContractVersion = seed.RequestContractVersion;
                    cron.RequestContractFingerprint = seed.RequestContractFingerprint;
                });
            Definition(cron.Retries != seed.Retries, () => cron.Retries = seed.Retries);
            Definition(!DefinedCronDefinition.IntsEqual(cron.RetryIntervals, seed.RetryIntervals),
                () => cron.RetryIntervals = seed.RetryIntervals?.ToArray());
            Definition(cron.TimeoutSeconds != seed.TimeoutSeconds, () => cron.TimeoutSeconds = seed.TimeoutSeconds);

            var definition = seed.Definition;
            if (definition == null)
                return new DefinedCronSeedChange(changed, definitionChanged);

            Definition(!string.Equals(Normalize(cron.TimeZoneId), definition.TimeZoneId, StringComparison.Ordinal),
                () => cron.TimeZoneId = definition.TimeZoneId);
            Definition(!string.Equals(Normalize(cron.SchedulePolicyName), definition.SchedulePolicyName, StringComparison.Ordinal)
                       || cron.BlockedOccurrenceBehavior != definition.BlockedOccurrenceBehavior, () =>
            {
                cron.SchedulePolicyName = definition.SchedulePolicyName;
                cron.BlockedOccurrenceBehavior = definition.BlockedOccurrenceBehavior;
            });
            Metadata(cron.SchedulePolicyVersion != definition.SchedulePolicyVersion,
                () => cron.SchedulePolicyVersion = definition.SchedulePolicyVersion);
            Definition(cron.MissedExecutionAction != definition.MissedExecutionAction
                       || cron.MaximumLateness != definition.MaximumLateness, () =>
            {
                cron.MissedExecutionAction = definition.MissedExecutionAction;
                cron.MaximumLateness = definition.MaximumLateness;
            });
            Definition(cron.CatchUpMode != definition.CatchUpMode, () => cron.CatchUpMode = definition.CatchUpMode);
            Definition(cron.SkipIfAlreadyRunning != definition.SkipIfAlreadyRunning,
                () => cron.SkipIfAlreadyRunning = definition.SkipIfAlreadyRunning);
            Definition(cron.OnStale != definition.OnStale, () => cron.OnStale = definition.OnStale);
            Definition(!DefinedCronDefinition.BytesEqual(cron.Request, definition.Request),
                () => cron.Request = definition.Request.ToArray());
            Definition(!DefinedCronDefinition.ChainEquals(cron.Chain, definition.Chain),
                () => cron.Chain = DefinedCronDefinition.CloneChain(definition.Chain));
            Metadata(!string.Equals(Normalize(cron.Description), definition.Description, StringComparison.Ordinal),
                () => cron.Description = definition.Description);

            return new DefinedCronSeedChange(changed, definitionChanged);
        }

        /// <summary>Sets every seeded field on a newly created cron row.</summary>
        internal static void Initialize(CronTickerEntity cron, DefinedCronTickerSeed seed)
        {
            cron.Expression = seed.Expression;
            cron.RequestContractVersion = seed.RequestContractVersion;
            cron.RequestContractFingerprint = seed.RequestContractFingerprint;
            cron.Retries = seed.Retries;
            cron.RetryIntervals = seed.RetryIntervals?.ToArray();
            cron.TimeoutSeconds = seed.TimeoutSeconds;
            cron.Request = Array.Empty<byte>();

            var definition = seed.Definition;
            if (definition == null) return;
            cron.TimeZoneId = definition.TimeZoneId;
            cron.SchedulePolicyName = definition.SchedulePolicyName;
            cron.SchedulePolicyVersion = definition.SchedulePolicyVersion;
            cron.BlockedOccurrenceBehavior = definition.BlockedOccurrenceBehavior;
            cron.MissedExecutionAction = definition.MissedExecutionAction;
            cron.MaximumLateness = definition.MaximumLateness;
            cron.CatchUpMode = definition.CatchUpMode;
            cron.SkipIfAlreadyRunning = definition.SkipIfAlreadyRunning;
            cron.OnStale = definition.OnStale;
            cron.Description = definition.Description;
            cron.Request = definition.Request.ToArray();
            cron.Chain = DefinedCronDefinition.CloneChain(definition.Chain);
            // The declared enabled state applies only when the cron is first created, so operators'
            // dashboard toggles survive later reconciliations.
            cron.IsEnabled = definition.IsEnabled;
        }

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
