using System;

namespace TickerQ.Utilities.Licensing
{
    /// <summary>
    /// Immutable, self-consistent snapshot of the offline license evaluation. Produced once at startup by
    /// the runtime and published through <see cref="TickerQLicenseStateProvider"/> so the Core enforcement
    /// path and the Dashboard both consume the identical result. Only safe, presentation-ready fields are
    /// exposed: the raw certificate, envelope, payload, signature, and public key are never surfaced here.
    /// </summary>
    public sealed class TickerQLicenseState
    {
        /// <summary>Fixed, safe portal link where a certificate can be obtained.</summary>
        public const string GetCertificateUrl = "https://license.tickerq.net/pricing";

        /// <summary>Fixed, safe portal link where an existing certificate can be renewed/reviewed.</summary>
        public const string RenewCertificateUrl = "https://license.tickerq.net/billing";

        /// <summary>The number of days before a certificate is considered <see cref="TickerQLicenseStatus.Expiring"/>.</summary>
        public const int ExpiringThresholdDays = 30;

        private TickerQLicenseState() { }

        public TickerQLicenseStatus Status { get; private set; }

        /// <summary>Whether TickerQ execution is permitted. Invalid or missing authority and expired Evaluations block execution.</summary>
        public bool ExecutionAllowed { get; private set; }

        /// <summary>A clear, human-readable explanation suitable for logs, the banner, and the About page.</summary>
        public string Message { get; private set; }

        /// <summary>Optional call-to-action label (e.g. "Get a certificate"), or null when no action applies.</summary>
        public string ActionLabel { get; private set; }

        /// <summary>Optional fixed, safe portal URL paired with <see cref="ActionLabel"/>, or null.</summary>
        public string ActionUrl { get; private set; }

        public Guid? LicenseId { get; private set; }
        public Guid? WorkspaceId { get; private set; }
        public string WorkspaceName { get; private set; }
        public string Plan { get; private set; }
        public string Kind { get; private set; }
        public bool IsEvaluation { get; private set; }
        public DateTimeOffset? IssuedAt { get; private set; }
        public DateTimeOffset? ExpiresAt { get; private set; }
        public int? DaysRemaining { get; private set; }
        public int? SchemaVersion { get; private set; }
        public string Algorithm { get; private set; }
        public string KeyId { get; private set; }
        public string AnchoredMinorLine { get; private set; }

        /// <summary>
        /// The state used before validation has completed. Fails closed: execution is blocked until the
        /// runtime license service publishes a validated result at startup.
        /// </summary>
        public static readonly TickerQLicenseState NotValidated = Missing();

        /// <summary>No certificate configured or present on disk. Execution is blocked.</summary>
        public static TickerQLicenseState Missing() => new TickerQLicenseState
        {
            Status = TickerQLicenseStatus.Missing,
            ExecutionAllowed = false,
            Message = "No TickerQ license certificate was found. TickerQ execution is disabled until a valid certificate is provided.",
            ActionLabel = "Get a certificate",
            ActionUrl = GetCertificateUrl,
        };

        /// <summary>A certificate was present but failed verification. Execution is blocked. No internal detail is leaked.</summary>
        public static TickerQLicenseState Invalid(string reason) => new TickerQLicenseState
        {
            Status = TickerQLicenseStatus.Invalid,
            ExecutionAllowed = false,
            Message = string.IsNullOrWhiteSpace(reason)
                ? "The TickerQ license certificate is invalid. TickerQ execution is disabled."
                : "The TickerQ license certificate is invalid (" + reason + "). TickerQ execution is disabled.",
            ActionLabel = "Get a certificate",
            ActionUrl = GetCertificateUrl,
        };

        /// <summary>
        /// Builds the state for a cryptographically verified certificate. Centralizes the message and
        /// call-to-action policy so Core and Dashboard never drift. <paramref name="status"/> must be one of
        /// <see cref="TickerQLicenseStatus.Active"/>, <see cref="TickerQLicenseStatus.Expiring"/>, or
        /// <see cref="TickerQLicenseStatus.Expired"/>.
        /// </summary>
        public static TickerQLicenseState ForCertificate(
            TickerQLicenseStatus status,
            bool isEvaluation,
            Guid licenseId,
            Guid workspaceId,
            string workspaceName,
            string plan,
            string kind,
            DateTimeOffset issuedAt,
            DateTimeOffset? expiresAt,
            int? daysRemaining,
            int schemaVersion,
            string algorithm,
            string keyId,
            string anchoredMinorLine)
        {
            var state = new TickerQLicenseState
            {
                Status = status,
                ExecutionAllowed = status != TickerQLicenseStatus.Expired || !isEvaluation,
                IsEvaluation = isEvaluation,
                LicenseId = licenseId,
                WorkspaceId = workspaceId,
                WorkspaceName = workspaceName,
                Plan = plan,
                Kind = kind,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                DaysRemaining = daysRemaining,
                SchemaVersion = schemaVersion,
                Algorithm = algorithm,
                KeyId = keyId,
                AnchoredMinorLine = anchoredMinorLine,
            };

            var expiryDate = expiresAt.HasValue
                ? expiresAt.Value.UtcDateTime.ToString("yyyy-MM-dd")
                : null;
            var days = daysRemaining ?? 0;

            switch (status)
            {
                case TickerQLicenseStatus.Expired:
                    state.Message = isEvaluation
                        ? "The TickerQ Evaluation certificate expired on " + expiryDate + ". TickerQ execution is disabled."
                        : string.IsNullOrWhiteSpace(anchoredMinorLine)
                            ? "The TickerQ " + plan + " certificate expired on " + expiryDate + ". TickerQ execution remains enabled. Renew the certificate."
                            : "The TickerQ " + plan + " certificate expired on " + expiryDate + ". TickerQ execution continues under its anchored " + anchoredMinorLine + " rights. Renew the certificate.";
                    break;
                case TickerQLicenseStatus.Expiring:
                    state.Message = isEvaluation
                        ? "TickerQ is running under a non-production Evaluation certificate; it expires in " + days + " day(s)."
                        : "The TickerQ license certificate expires in " + days + " day(s).";
                    break;
                default: // Active
                    state.Message = isEvaluation
                        ? "TickerQ is running under a non-production Evaluation certificate. It is not licensed for production use."
                        : "The TickerQ license certificate is active.";
                    break;
            }

            // Fixed, safe call-to-action. Every expired certificate points at renewal. An in-force
            // Evaluation points at purchase; expiring paid certificates point at renewal.
            if (status == TickerQLicenseStatus.Expired)
            {
                state.ActionLabel = "Renew certificate";
                state.ActionUrl = RenewCertificateUrl;
            }
            else if (isEvaluation)
            {
                state.ActionLabel = "Get a certificate";
                state.ActionUrl = GetCertificateUrl;
            }
            else if (status == TickerQLicenseStatus.Expiring)
            {
                state.ActionLabel = "Renew certificate";
                state.ActionUrl = RenewCertificateUrl;
            }

            return state;
        }

        internal TickerQLicenseState RefreshTemporalStatus(DateTimeOffset now)
        {
            if (Status == TickerQLicenseStatus.Expired || !ExecutionAllowed || !ExpiresAt.HasValue || ExpiresAt.Value > now)
                return this;

            return ForCertificate(
                TickerQLicenseStatus.Expired,
                IsEvaluation,
                LicenseId!.Value,
                WorkspaceId!.Value,
                WorkspaceName,
                Plan,
                Kind,
                IssuedAt!.Value,
                ExpiresAt,
                (int)Math.Ceiling((ExpiresAt.Value - now).TotalDays),
                SchemaVersion!.Value,
                Algorithm,
                KeyId,
                AnchoredMinorLine);
        }
    }
}
