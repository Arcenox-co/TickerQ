using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Utilities.Models
{
    /// <summary>Versioned, bounded identity for application-owned Cron definitions.</summary>
    public static class CronSeedIdentity
    {
        private const string DeterministicIdNamespace = "tickerq:cron-seed:v1:";
        private const string PersistedKeyPrefix = "tq:cron-seed:v2:";
        public const int MaxIdentityUtf8Bytes = 512;
        public const int PersistedKeyLength = 80;

        /// <summary>
        /// Builds a fixed-size persisted key from an injectively length-prefixed canonical identity.
        /// The digest bounds store indexes while the framing prevents delimiter ambiguity.
        /// </summary>
        public static string SeedKey(string applicationNamespace, string stableDefinitionId)
        {
            var application = EncodePart(applicationNamespace, nameof(applicationNamespace));
            var definition = EncodePart(stableDefinitionId, nameof(stableDefinitionId));
            return BuildSeedKey(application, definition);
        }

        private static string BuildSeedKey(byte[] application, byte[] definition)
        {
            var canonical = new byte[1 + 4 + application.Length + 4 + definition.Length];
            canonical[0] = 2;
            BinaryPrimitives.WriteInt32BigEndian(canonical.AsSpan(1, 4), application.Length);
            application.CopyTo(canonical.AsSpan(5));
            var definitionOffset = 5 + application.Length;
            BinaryPrimitives.WriteInt32BigEndian(canonical.AsSpan(definitionOffset, 4), definition.Length);
            definition.CopyTo(canonical.AsSpan(definitionOffset + 4));
            return PersistedKeyPrefix + Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
        }

        /// <summary>
        /// Compares identity text after Unicode normalization (NFC). Function names stored by earlier releases
        /// were not normalized, so a released row still matches its normalized declaration.
        /// </summary>
        public static bool CanonicallyEquals(string left, string right)
            => left != null && right != null &&
               string.Equals(left.Normalize(NormalizationForm.FormC),
                   right.Normalize(NormalizationForm.FormC), StringComparison.Ordinal);

        /// <summary>The legacy, pre-namespace ownership key retained only for internal adoption compatibility.</summary>
        internal static string LegacySeedKeyForFunction(string function)
            => function ?? throw new ArgumentNullException(nameof(function));

        /// <summary>The deterministic primary key for a newly created seed row.</summary>
        public static Guid DeterministicId(string seedKey)
        {
            if (seedKey == null) throw new ArgumentNullException(nameof(seedKey));
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(DeterministicIdNamespace + seedKey));
            return new Guid(bytes.AsSpan(0, 16));
        }

        private static byte[] EncodePart(string value, string parameterName)
            => Encoding.UTF8.GetBytes(ValidatePart(value, parameterName));

        /// <summary>Canonicalizes and bounds one identity component without constructing a key.</summary>
        internal static string CanonicalizePart(string value, string parameterName)
            => ValidatePart(value, parameterName);

        private static string ValidatePart(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Cron seed identity parts must be non-empty.", parameterName);
            var normalized = value.Trim().Normalize(NormalizationForm.FormC);
            if (Encoding.UTF8.GetByteCount(normalized) > MaxIdentityUtf8Bytes)
                throw new ArgumentException(
                    $"Cron seed identity parts must be at most {MaxIdentityUtf8Bytes} UTF-8 bytes.", parameterName);
            return normalized;
        }
    }
}
