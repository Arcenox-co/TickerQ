using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// The authoritative desired-state view of the code-owned cron seeds for a single reconciliation
    /// pass, derived purely from the <see cref="DefinedCronTickerSeed"/>s the initializer projects from
    /// local code definitions. It exists to freeze the reconciliation semantics that were previously
    /// ambiguous: providers used to pair the seed array with the <em>global runtime function registry</em>
    /// (<c>TickerFunctionProvider.TickerFunctions</c>) to decide which persisted seeded rows were orphaned.
    /// That conflated "the function is still registered" with "the code still wants a seeded schedule",
    /// so removing only a cron expression (while the function stayed registered) left the stale seeded
    /// schedule firing forever.
    /// </summary>
    /// <remarks>
    /// Orphan detection compares persisted <em>code-owned</em> rows to <see cref="DesiredSeedFunctions"/>
    /// — the desired local seed keys — never to the live registry. A blocked seed
    /// (<see cref="DefinedCronTickerSeed.CanSeed"/> == false) is represented deliberately: it is present in
    /// <see cref="Seeds"/> but excluded from <see cref="DesiredSeedFunctions"/>, so any previously
    /// auto-seeded row for it is retired while user/dashboard rows (no seed ownership) are untouched by the
    /// provider's independent InitIdentifier/SeedKey filter.
    /// </remarks>
    public sealed class DefinedCronSeedManifest
    {
        private readonly HashSet<string> _desiredSeedFunctions;
        private readonly HashSet<string> _desiredSeedKeys;
        private readonly DefinedCronTickerSeed[] _seeds;
        private readonly IReadOnlyCollection<string> _desiredSeedFunctionsView;
        private readonly IReadOnlyCollection<string> _desiredSeedKeysView;
        private readonly IReadOnlyDictionary<string, string> _legacyOwnershipByFunction;

        public DefinedCronSeedManifest(IReadOnlyCollection<DefinedCronTickerSeed> seeds)
            : this(null, seeds)
        {
        }

        public DefinedCronSeedManifest(string applicationNamespace, IReadOnlyCollection<DefinedCronTickerSeed> seeds)
            : this(applicationNamespace, seeds, null)
        {
        }

        /// <summary>
        /// Creates a namespaced manifest with an explicit, store-wide ownership decision for legacy
        /// pre-namespace rows. Each key is a function name and each value is the one application namespace
        /// permitted to adopt that function's legacy row in place.
        /// </summary>
        public DefinedCronSeedManifest(string applicationNamespace, IReadOnlyCollection<DefinedCronTickerSeed> seeds,
            IReadOnlyDictionary<string, string> legacyOwnershipByFunction)
        {
            if (seeds == null) throw new ArgumentNullException(nameof(seeds));
            if (applicationNamespace != null && string.IsNullOrWhiteSpace(applicationNamespace))
                throw new ArgumentException("Application namespace cannot be empty.", nameof(applicationNamespace));

            ApplicationNamespace = applicationNamespace == null
                ? null
                : CronSeedIdentity.CanonicalizePart(applicationNamespace, nameof(applicationNamespace));

            var ownershipSnapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            if (legacyOwnershipByFunction != null)
            {
                foreach (var (function, owner) in legacyOwnershipByFunction)
                {
                    var canonicalFunction = CronSeedIdentity.CanonicalizePart(
                        function, nameof(legacyOwnershipByFunction));
                    var canonicalOwner = CronSeedIdentity.CanonicalizePart(
                        owner, nameof(legacyOwnershipByFunction));
                    if (ownershipSnapshot.TryGetValue(canonicalFunction, out var existingOwner))
                    {
                        if (!string.Equals(existingOwner, canonicalOwner, StringComparison.Ordinal))
                            throw new ArgumentException(
                                $"Legacy ownership contains conflicting owners for normalized function '{canonicalFunction}'.",
                                nameof(legacyOwnershipByFunction));
                        continue;
                    }
                    ownershipSnapshot.Add(canonicalFunction, canonicalOwner);
                }
            }
            _legacyOwnershipByFunction = new ReadOnlyDictionary<string, string>(ownershipSnapshot);

            var seedSnapshot = new List<DefinedCronTickerSeed>(seeds.Count);
            var seedsByStableIdentity = new Dictionary<string, DefinedCronTickerSeed>(StringComparer.Ordinal);
            var seedsByFunction = new Dictionary<string, DefinedCronTickerSeed>(StringComparer.Ordinal);
            foreach (var seed in seeds)
            {
                var canonical = DefinedCronTickerSeed.CanonicalSnapshot(seed, nameof(seeds));
                if (seedsByFunction.TryGetValue(canonical.Function, out var existingFunctionSeed))
                {
                    if (existingFunctionSeed.Equals(canonical))
                        continue;
                    throw new ArgumentException(
                        $"The defined Cron manifest contains conflicting seeds for normalized function '{canonical.Function}'.",
                        nameof(seeds));
                }
                if (seedsByStableIdentity.TryGetValue(canonical.StableDefinitionId, out var existing))
                {
                    if (existing.Equals(canonical))
                        continue;
                    throw new ArgumentException(
                        $"The defined Cron manifest contains conflicting seeds for normalized stable definition identity '{canonical.StableDefinitionId}'.",
                        nameof(seeds));
                }
                seedsByFunction.Add(canonical.Function, canonical);
                seedsByStableIdentity.Add(canonical.StableDefinitionId, canonical);
                seedSnapshot.Add(canonical);
            }

            _seeds = seedSnapshot.ToArray();
            _desiredSeedFunctions = _seeds
                .Where(s => s.CanSeed)
                .Select(s => s.Function)
                .ToHashSet(StringComparer.Ordinal);
            _desiredSeedKeys = ApplicationNamespace == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : _seeds.Where(s => s.CanSeed)
                    .Select(s => CronSeedIdentity.SeedKey(ApplicationNamespace, s.StableDefinitionId))
                    .ToHashSet(StringComparer.Ordinal);
            _desiredSeedFunctionsView = Array.AsReadOnly(_desiredSeedFunctions.ToArray());
            _desiredSeedKeysView = Array.AsReadOnly(_desiredSeedKeys.ToArray());
        }

        public string ApplicationNamespace { get; }
        public bool IsLegacyGlobal => ApplicationNamespace == null;

        /// <summary>Every projected seed for this pass — both desired (seedable) and blocked.</summary>
        public IReadOnlyList<DefinedCronTickerSeed> Seeds => Array.AsReadOnly(
            _seeds.Select(CloneSeed).ToArray());

        /// <summary>
        /// The function keys the code authoritatively wants a live seeded cron for (a present cron
        /// expression and a seedable contract). This is the sole orphan-comparison set.
        /// </summary>
        public IReadOnlyCollection<string> DesiredSeedFunctions => _desiredSeedFunctionsView;
        public IReadOnlyCollection<string> DesiredSeedKeys => _desiredSeedKeysView;
        public IReadOnlyDictionary<string, string> LegacyOwnershipByFunction => _legacyOwnershipByFunction;

        public bool TryGetLegacyOwner(string function, out string ownerNamespace)
            => _legacyOwnershipByFunction.TryGetValue(function, out ownerNamespace);

        public bool MayAdoptLegacy(string function)
            => TryGetLegacyOwner(function, out var owner)
               && string.Equals(owner, ApplicationNamespace, StringComparison.Ordinal);

        /// <summary>
        /// True when a persisted code-owned (seeded) row for <paramref name="function"/> is no longer
        /// desired — i.e. its function is absent from <see cref="DesiredSeedFunctions"/>. Callers still
        /// gate this on their own code-ownership marker (InitIdentifier/SeedKey) so user/dashboard rows
        /// are never considered.
        /// </summary>
        public bool IsOrphanedSeedFunction(string function) => !_desiredSeedFunctions.Contains(function);

        public string SeedKeyFor(DefinedCronTickerSeed seed) => IsLegacyGlobal
            ? CronSeedIdentity.SeedKeyForFunction(seed.Function)
            : CronSeedIdentity.SeedKey(ApplicationNamespace, seed.StableDefinitionId);

        public bool IsOrphanedSeedKey(string seedKey) => !_desiredSeedKeys.Contains(seedKey);

        private static DefinedCronTickerSeed CloneSeed(DefinedCronTickerSeed seed)
            => new(seed.Function, seed.Expression, seed.RequestContractVersion,
                seed.RequestContractFingerprint, seed.CanSeed, seed.StableDefinitionId, seed.Retries,
                seed.RetryIntervals?.ToArray(), seed.TimeoutSeconds);
    }
}
