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
        private const int MaxCanonicalVariantsPerPart = 256;
        private const int MaxAcceptedSeedKeys = 4096;
        private const int MaxLegacyAdoptionKeys = MaxAcceptedSeedKeys * 3;
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

        /// <summary>Canonical and pre-NFC v2 hashes accepted for in-place namespaced adoption.</summary>
        public static string[] AcceptedSeedKeys(string applicationNamespace, string stableDefinitionId)
        {
            var application = ValidatePart(applicationNamespace, nameof(applicationNamespace));
            var definition = ValidatePart(stableDefinitionId, nameof(stableDefinitionId));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var app in CanonicalVariants(application))
            foreach (var def in CanonicalVariants(definition))
            {
                keys.Add(BuildSeedKey(Encoding.UTF8.GetBytes(app), Encoding.UTF8.GetBytes(def)));
                if (keys.Count > MaxAcceptedSeedKeys)
                    throw new InvalidOperationException(
                        "Cron seed compatibility identity has too many canonical representations to adopt safely.");
            }
            return keys.ToArray();
        }

        private static string[] CanonicalVariants(string canonical)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { canonical };
            var pending = new Queue<string>();
            var decomposed = canonical.Normalize(NormalizationForm.FormD);
            if (seen.Add(decomposed)) pending.Enqueue(decomposed);
            else pending.Enqueue(canonical);

            while (pending.Count > 0)
            {
                var value = pending.Dequeue();
                var boundaries = new List<int> { 0 };
                for (var index = 0; index < value.Length;)
                {
                    index += char.IsSurrogatePair(value, index) ? 2 : 1;
                    boundaries.Add(index);
                }

                bool TryAdd(string candidate)
                {
                    if (!string.Equals(candidate.Normalize(NormalizationForm.FormC), canonical,
                            StringComparison.Ordinal) || !seen.Add(candidate))
                        return false;
                    if (seen.Count > MaxCanonicalVariantsPerPart)
                        throw new InvalidOperationException(
                            "Cron seed identity has too many canonical representations to adopt safely.");
                    pending.Enqueue(candidate);
                    return true;
                }

                // Pre-normalization callers could supply canonically equivalent combining marks in
                // noncanonical order. Adjacent swaps plus NFC composition traverse that complete,
                // bounded equivalence class; the normalization check rejects non-equivalent swaps.
                for (var index = 0; index < boundaries.Count - 2; index++)
                {
                    var firstStart = boundaries[index];
                    var firstEnd = boundaries[index + 1];
                    var secondEnd = boundaries[index + 2];
                    var candidate = string.Concat(value.AsSpan(0, firstStart),
                        value.AsSpan(firstEnd, secondEnd - firstEnd),
                        value.AsSpan(firstStart, firstEnd - firstStart),
                        value.AsSpan(secondEnd));
                    TryAdd(candidate);
                }

                for (var start = 0; start < boundaries.Count - 1; start++)
                for (var end = start + 1; end < boundaries.Count; end++)
                {
                    var offset = boundaries[start];
                    var length = boundaries[end] - offset;
                    var composed = value.Substring(offset, length).Normalize(NormalizationForm.FormC);
                    if (composed.Length == length &&
                        string.CompareOrdinal(composed, 0, value, offset, length) == 0)
                        continue;
                    var candidate = string.Concat(value.AsSpan(0, offset), composed,
                        value.AsSpan(offset + length));
                    TryAdd(candidate);
                }
            }

            return seen.ToArray();
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

        /// <summary>Keys written by pre-v2 implementations, in adoption preference order.</summary>
        public static string[] LegacyAdoptionKeys(string applicationNamespace, string stableDefinitionId)
        {
            var application = ValidatePart(applicationNamespace, nameof(applicationNamespace));
            var definition = ValidatePart(stableDefinitionId, nameof(stableDefinitionId));
            var applicationVariants = CanonicalVariants(application);
            var definitionVariants = CanonicalVariants(definition);
            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var app in applicationVariants)
            foreach (var def in definitionVariants)
            {
                keys.Add($"{app}:{def}");
                if (keys.Count > MaxLegacyAdoptionKeys)
                    throw new InvalidOperationException(
                        "Cron seed legacy compatibility identity has too many canonical representations to adopt safely.");
            }

            foreach (var def in definitionVariants)
                keys.Add(def);
            foreach (var key in AcceptedSeedKeys(application, definition))
                keys.Add(key);

            if (keys.Count > MaxLegacyAdoptionKeys)
                throw new InvalidOperationException(
                    "Cron seed legacy compatibility identity has too many canonical representations to adopt safely.");
            return keys.ToArray();
        }

        /// <summary>Compares persisted pre-NFC identity text with canonical manifest text.</summary>
        public static bool CanonicallyEquals(string left, string right)
            => left != null && right != null &&
               string.Equals(left.Normalize(NormalizationForm.FormC),
                   right.Normalize(NormalizationForm.FormC), StringComparison.Ordinal);

        /// <summary>The legacy, pre-namespace ownership key retained only for adoption compatibility.</summary>
        [Obsolete("Use SeedKey(applicationNamespace, stableDefinitionId) for shared-store-safe identity.")]
        public static string SeedKeyForFunction(string function)
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
