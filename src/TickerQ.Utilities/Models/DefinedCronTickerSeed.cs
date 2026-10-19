using System;
using System.Linq;

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
            int? requestContractVersion,
            string requestContractFingerprint,
            bool canSeed)
            : this(function, expression, requestContractVersion, requestContractFingerprint, canSeed,
                stableDefinitionId: null, retries: 0, retryIntervals: null, timeoutSeconds: null)
        {
        }

        public DefinedCronTickerSeed(
            string function,
            string expression,
            int? requestContractVersion = null,
            string requestContractFingerprint = null,
            bool canSeed = true,
            string stableDefinitionId = null,
            int retries = 0,
            int[] retryIntervals = null,
            int? timeoutSeconds = null)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            Expression = expression;
            RequestContractVersion = requestContractVersion;
            RequestContractFingerprint = requestContractFingerprint;
            CanSeed = canSeed;
            StableDefinitionId = string.IsNullOrWhiteSpace(stableDefinitionId) ? Function : stableDefinitionId.Trim();
            Retries = retries;
            RetryIntervals = retryIntervals;
            TimeoutSeconds = timeoutSeconds;
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

        /// <summary>Stable identity within an application namespace; defaults to <see cref="Function"/>.</summary>
        public string StableDefinitionId { get; }

        public int Retries { get; }
        public int[] RetryIntervals { get; }
        public int? TimeoutSeconds { get; }

        /// <summary>
        /// Validates and canonicalizes the complete seed contract at the manifest ingress boundary.
        /// Keeping this in one place ensures every manifest constructor and provider sees equivalent,
        /// detached values rather than caller-owned or merely syntactically valid input.
        /// </summary>
        internal static DefinedCronTickerSeed CanonicalSnapshot(DefinedCronTickerSeed seed, string parameterName)
        {
            var function = CronSeedIdentity.CanonicalizePart(seed.Function, parameterName);
            var stableDefinitionId = CronSeedIdentity.CanonicalizePart(seed.StableDefinitionId, parameterName);
            var expression = CronExpression.Parse(seed.Expression).Value;

            if (seed.RequestContractVersion is <= 0)
                throw new ArgumentOutOfRangeException(parameterName, seed.RequestContractVersion,
                    "Defined Cron request contract versions must be positive.");

            string fingerprint = null;
            if (seed.RequestContractFingerprint != null)
            {
                if (string.IsNullOrWhiteSpace(seed.RequestContractFingerprint))
                    throw new ArgumentException(
                        "Defined Cron request contract fingerprints must be non-empty when supplied.",
                        parameterName);
                if (seed.RequestContractVersion == null)
                    throw new ArgumentException(
                        "A defined Cron request contract fingerprint requires a contract version.",
                        parameterName);
                fingerprint = seed.RequestContractFingerprint.Trim();
            }

            if (seed.Retries < 0 || seed.Retries > DefinedCronExecutionLimits.MaxRetries)
                throw new ArgumentOutOfRangeException(parameterName, seed.Retries,
                    $"Defined Cron retries must be between 0 and {DefinedCronExecutionLimits.MaxRetries}.");

            var retryIntervals = seed.RetryIntervals?.ToArray();
            if (retryIntervals?.Any(interval =>
                    interval < 0 || interval > DefinedCronExecutionLimits.MaxRetryIntervalSeconds) == true)
                throw new ArgumentOutOfRangeException(parameterName,
                    $"Defined Cron retry intervals must be between 0 and {DefinedCronExecutionLimits.MaxRetryIntervalSeconds} seconds.");

            if (seed.TimeoutSeconds > DefinedCronExecutionLimits.MaxTimeoutSeconds)
                throw new ArgumentOutOfRangeException(parameterName, seed.TimeoutSeconds,
                    $"Defined Cron timeout must not exceed {DefinedCronExecutionLimits.MaxTimeoutSeconds} seconds.");

            // CronTickerEntity defines every non-positive timeout as the same explicit no-timeout policy.
            // Collapse those equivalent representations so manifests compare deterministically.
            var timeoutSeconds = seed.TimeoutSeconds is <= 0 ? 0 : seed.TimeoutSeconds;

            return new DefinedCronTickerSeed(function, expression, seed.RequestContractVersion, fingerprint,
                seed.CanSeed, stableDefinitionId, seed.Retries, retryIntervals, timeoutSeconds);
        }

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
               && CanSeed == other.CanSeed
               && string.Equals(StableDefinitionId, other.StableDefinitionId, StringComparison.Ordinal)
               && Retries == other.Retries
               && ((RetryIntervals == null && other.RetryIntervals == null)
                   || (RetryIntervals != null && other.RetryIntervals != null
                       && RetryIntervals.AsSpan().SequenceEqual(other.RetryIntervals)))
               && TimeoutSeconds == other.TimeoutSeconds;

        public override bool Equals(object obj) => obj is DefinedCronTickerSeed other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Function, StringComparer.Ordinal);
            hash.Add(Expression, StringComparer.Ordinal);
            hash.Add(RequestContractVersion);
            hash.Add(RequestContractFingerprint, StringComparer.Ordinal);
            hash.Add(CanSeed);
            hash.Add(StableDefinitionId, StringComparer.Ordinal);
            hash.Add(Retries);
            if (RetryIntervals != null)
                foreach (var interval in RetryIntervals)
                    hash.Add(interval);
            hash.Add(TimeoutSeconds);
            return hash.ToHashCode();
        }
    }
}
