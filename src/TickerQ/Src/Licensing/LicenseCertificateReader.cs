using System;
using System.IO;
using System.Text.Json;
using NSec.Cryptography;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Licensing
{
    /// <summary>
    /// Offline reader/verifier for a <c>.tqlicense</c> certificate. It is completely side-effect-free and
    /// never throws: any failure (missing file, malformed envelope/payload, untrusted key, signature
    /// mismatch, incoherent contract) collapses to a <see cref="TickerQLicenseState"/> the host can start
    /// with and display for diagnosis. The signature is verified against a source-pinned trusted key only
    /// (see <see cref="TrustedLicenseKeys"/>); certificate- or configuration-provided keys are never trusted.
    /// Expired certificates remain readable — expiry is surfaced as a blocking state, not a read failure.
    /// </summary>
    internal static class LicenseCertificateReader
    {
        internal const string DefaultFileName = "tickerq.tqlicense";

        // The commercial-transition Functional Minor Line a paid v4 Full certificate must anchor to.
        private const string V4AnchoredMinorLine = "5.x";

        private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

        /// <summary>
        /// Resolves the certificate path (defaulting to <c>tickerq.tqlicense</c> in the content root),
        /// reads it, and evaluates it into a license state. A missing file yields
        /// <see cref="TickerQLicenseState.Missing"/> rather than an error.
        /// </summary>
        public static TickerQLicenseState Read(string configuredPath, string contentRootPath, DateTimeOffset now)
        {
            string path;
            try
            {
                path = ResolvePath(configuredPath, contentRootPath);
            }
            catch (Exception)
            {
                return TickerQLicenseState.Invalid("invalid certificate path");
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return TickerQLicenseState.Missing();

            byte[] contents;
            try
            {
                contents = File.ReadAllBytes(path);
            }
            catch (Exception)
            {
                return TickerQLicenseState.Invalid("certificate could not be read");
            }

            return Evaluate(contents, now);
        }

        private static string ResolvePath(string configuredPath, string contentRootPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return string.IsNullOrWhiteSpace(contentRootPath)
                    ? DefaultFileName
                    : Path.Combine(contentRootPath, DefaultFileName);
            }

            if (Path.IsPathRooted(configuredPath))
                return configuredPath;

            return string.IsNullOrWhiteSpace(contentRootPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(configuredPath, contentRootPath);
        }

        private static TickerQLicenseState Evaluate(byte[] contents, DateTimeOffset now)
        {
            LicenseEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize(contents, LicenseJsonContext.Default.LicenseEnvelope);
            }
            catch (JsonException)
            {
                return TickerQLicenseState.Invalid("malformed certificate");
            }

            if (envelope == null
                || (envelope.SchemaVersion != 2 && envelope.SchemaVersion != 3 && envelope.SchemaVersion != 4)
                || !string.Equals(envelope.Algorithm, "Ed25519", StringComparison.Ordinal))
                return TickerQLicenseState.Invalid("unsupported certificate");

            // Trust only a source-pinned key, resolved by the envelope key id.
            var trustedPublicKey = TrustedLicenseKeys.TryGetPublicKey(envelope.KeyId);
            if (trustedPublicKey == null)
                return TickerQLicenseState.Invalid("untrusted signing key");

            byte[] payloadBytes;
            byte[] signatureBytes;
            try
            {
                payloadBytes = LicenseBase64Url.Decode(envelope.Payload);
                signatureBytes = LicenseBase64Url.Decode(envelope.Signature);
            }
            catch (FormatException)
            {
                return TickerQLicenseState.Invalid("malformed certificate encoding");
            }

            PublicKey publicKey;
            try
            {
                publicKey = PublicKey.Import(Algorithm, trustedPublicKey, KeyBlobFormat.RawPublicKey);
            }
            catch (Exception)
            {
                return TickerQLicenseState.Invalid("untrusted signing key");
            }

            if (!Algorithm.Verify(publicKey, payloadBytes, signatureBytes))
                return TickerQLicenseState.Invalid("signature mismatch");

            LicensePayload payload;
            try
            {
                payload = JsonSerializer.Deserialize(payloadBytes, LicenseJsonContext.Default.LicensePayload);
            }
            catch (JsonException)
            {
                return TickerQLicenseState.Invalid("malformed certificate payload");
            }

            if (payload == null)
                return TickerQLicenseState.Invalid("malformed certificate payload");

            // The signed payload must agree with its envelope on schema and key.
            if (payload.SchemaVersion != envelope.SchemaVersion)
                return TickerQLicenseState.Invalid("certificate schema mismatch");
            if (!string.Equals(payload.KeyId, envelope.KeyId, StringComparison.Ordinal))
                return TickerQLicenseState.Invalid("certificate key mismatch");

            var contractFailure = ValidateContract(payload);
            if (contractFailure != null)
                return TickerQLicenseState.Invalid(contractFailure);

            return BuildState(envelope, payload, now);
        }

        // Mirrors the portal's frozen schema/kind contract. Returns a short reason on failure, or null when
        // the certificate is structurally valid. Temporal expiry is intentionally NOT evaluated here — an
        // expired certificate is still structurally valid and must remain readable.
        private static string ValidateContract(LicensePayload payload)
        {
            if (payload.LicenseId == Guid.Empty || payload.WorkspaceId == Guid.Empty)
                return "incomplete certificate identity";
            if (string.IsNullOrWhiteSpace(payload.WorkspaceName)
                || !string.Equals(payload.WorkspaceName, payload.WorkspaceName.Trim(), StringComparison.Ordinal))
                return "invalid workspace name";
            if (!Enum.IsDefined(typeof(LicensePlan), payload.Plan) || !Enum.IsDefined(typeof(LicenseKind), payload.LicenseKind))
                return "unsupported plan or kind";
            if (payload.IssuedAt == default)
                return "invalid issue date";
            if (payload.RuntimeExpiresAt.HasValue && payload.RuntimeExpiresAt.Value <= payload.IssuedAt)
                return "incoherent certificate dates";
            if (!payload.RuntimeExpiresAt.HasValue && !IsPerpetualCommunity(payload))
                return "missing runtime expiry";

            switch (payload.SchemaVersion, payload.LicenseKind)
            {
                case (2, LicenseKind.Full):
                case (3, LicenseKind.Full):
                    // Legacy full: a one-year runtime when present; historical perpetual Community may omit it.
                    if (payload.RuntimeExpiresAt.HasValue
                        && payload.RuntimeExpiresAt.Value != payload.IssuedAt.AddYears(1))
                        return "invalid full-license term";
                    return null;
                case (3, LicenseKind.Trial):
                    if (payload.Plan != LicensePlan.Business)
                        return "trial license must be Business";
                    if (payload.RuntimeExpiresAt != payload.IssuedAt.AddDays(15))
                        return "invalid trial term";
                    return null;
                case (4, LicenseKind.Evaluation):
                    if (payload.Plan != LicensePlan.Business)
                        return "evaluation license must be Business";
                    if (payload.RuntimeExpiresAt != payload.IssuedAt.AddDays(30))
                        return "invalid evaluation term";
                    return null;
                case (4, LicenseKind.Full):
                    if (payload.Plan == LicensePlan.Community)
                        return "paid license must not be Community";
                    if (!string.Equals(payload.AnchoredMinorLine, V4AnchoredMinorLine, StringComparison.Ordinal))
                        return "invalid anchored minor line";
                    return null;
                default:
                    return "unsupported schema or kind";
            }
        }

        private static bool IsPerpetualCommunity(LicensePayload payload) =>
            payload.SchemaVersion == 2
            && payload.Plan == LicensePlan.Community
            && payload.LicenseKind == LicenseKind.Full
            && !payload.RuntimeExpiresAt.HasValue;

        private static TickerQLicenseState BuildState(LicenseEnvelope envelope, LicensePayload payload, DateTimeOffset now)
        {
            var isEvaluation = payload.LicenseKind == LicenseKind.Evaluation;
            var expiresAt = payload.RuntimeExpiresAt;

            TickerQLicenseStatus status;
            int? daysRemaining;
            if (!expiresAt.HasValue)
            {
                // Perpetual Community: no runtime boundary.
                status = TickerQLicenseStatus.Active;
                daysRemaining = null;
            }
            else
            {
                var remaining = expiresAt.Value - now;
                daysRemaining = (int)Math.Ceiling(remaining.TotalDays);
                if (expiresAt.Value <= now)
                    status = TickerQLicenseStatus.Expired;
                else if (remaining <= TimeSpan.FromDays(TickerQLicenseState.ExpiringThresholdDays))
                    status = TickerQLicenseStatus.Expiring;
                else
                    status = TickerQLicenseStatus.Active;
            }

            return TickerQLicenseState.ForCertificate(
                status,
                isEvaluation,
                payload.LicenseId,
                payload.WorkspaceId,
                payload.WorkspaceName,
                payload.Plan.ToString(),
                payload.LicenseKind.ToString(),
                payload.IssuedAt,
                expiresAt,
                daysRemaining,
                envelope.SchemaVersion,
                envelope.Algorithm,
                envelope.KeyId,
                payload.AnchoredMinorLine);
        }
    }
}
