using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// A single code-defined cron ticker to seed, carrying the authoritative request-contract identity
    /// resolved from the function's <see cref="TickerFunctionDescriptor"/> at seed time. Replaces the
    /// old <c>(Function, Expression)</c> tuple so seeding no longer drops contract identity: providers
    /// stamp <see cref="RequestContractVersion"/>/<see cref="RequestContractFingerprint"/> onto new rows
    /// and reconcile them onto existing seeded rows, keeping seeded crons under execution-time drift
    /// enforcement instead of the legacy always-executable fallback.
    /// </summary>
    /// <remarks>
    /// <see cref="CanSeed"/> is false when an empty code-defined payload cannot satisfy the contract.
    /// Providers use that command to remove previously auto-seeded rows without touching user-created rows.
    /// </remarks>
    public readonly struct DefinedCronTickerSeed : IEquatable<DefinedCronTickerSeed>
    {
        public DefinedCronTickerSeed(
            string function,
            string expression,
            int? requestContractVersion = null,
            string requestContractFingerprint = null,
            bool canSeed = true)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            Expression = expression;
            RequestContractVersion = requestContractVersion;
            RequestContractFingerprint = requestContractFingerprint;
            CanSeed = canSeed;
        }

        /// <summary>Function name the cron targets (bare local name, or qualified <c>bare@node</c> remote).</summary>
        public string Function { get; }

        /// <summary>Final cron expression to seed.</summary>
        public string Expression { get; }

        /// <summary>Authoritative contract version to stamp, or null when identity is unknown (legacy).</summary>
        public int? RequestContractVersion { get; }

        /// <summary>Authoritative request-schema fingerprint to stamp, or null for request-less/schemaless contracts.</summary>
        public string RequestContractFingerprint { get; }

        /// <summary>Whether providers may create or reconcile the auto-seeded row.</summary>
        public bool CanSeed { get; }

        /// <summary>
        /// True when a persisted row already carries exactly this seed's contract identity, so a
        /// provider can skip an otherwise-needless reconcile write.
        /// </summary>
        public bool MatchesIdentity(int? persistedVersion, string persistedFingerprint)
            => RequestContractVersion == persistedVersion
               && string.Equals(RequestContractFingerprint, persistedFingerprint, StringComparison.Ordinal);

        public bool Equals(DefinedCronTickerSeed other)
            => string.Equals(Function, other.Function, StringComparison.Ordinal)
               && string.Equals(Expression, other.Expression, StringComparison.Ordinal)
               && RequestContractVersion == other.RequestContractVersion
               && string.Equals(RequestContractFingerprint, other.RequestContractFingerprint, StringComparison.Ordinal)
               && CanSeed == other.CanSeed;

        public override bool Equals(object obj) => obj is DefinedCronTickerSeed other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Function, StringComparer.Ordinal);
            hash.Add(Expression, StringComparer.Ordinal);
            hash.Add(RequestContractVersion);
            hash.Add(RequestContractFingerprint, StringComparer.Ordinal);
            hash.Add(CanSeed);
            return hash.ToHashCode();
        }
    }
}
