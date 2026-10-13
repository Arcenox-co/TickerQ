using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Immutable physical identity for one application's runtime state. The display namespace is
    /// retained for diagnostics while <see cref="StorageKey"/> is bounded and safe for store keys.
    /// </summary>
    public sealed class TickerQRuntimePartition : IEquatable<TickerQRuntimePartition>
    {
        private const string StorageKeyPrefix = "tq:runtime:v1:";
        private static readonly byte[] LegacyIdentity = Encoding.UTF8.GetBytes("tickerq:legacy-global:v1");

        private TickerQRuntimePartition()
        {
            ApplicationNamespace = null;
            StorageKey = BuildStorageKey(LegacyIdentity);
            IsLegacyGlobal = true;
        }

        public TickerQRuntimePartition(string applicationNamespace)
        {
            if (applicationNamespace == null)
                throw new ArgumentNullException(nameof(applicationNamespace));
            ApplicationNamespace = CronSeedIdentity.CanonicalizePart(
                applicationNamespace.Trim().Normalize(NormalizationForm.FormC), nameof(applicationNamespace));
            StorageKey = BuildStorageKey(Encoding.UTF8.GetBytes(ApplicationNamespace));
        }

        /// <summary>The sole namespace-less partition, valid only for queue-only compatibility.</summary>
        public static TickerQRuntimePartition LegacyGlobal { get; } = new();

        public string ApplicationNamespace { get; }
        public string StorageKey { get; }
        public bool IsLegacyGlobal { get; }

        internal static TickerQRuntimePartition Bind(string applicationNamespace, bool schedulerEnabled)
        {
            if (string.IsNullOrWhiteSpace(applicationNamespace))
            {
                if (schedulerEnabled)
                    throw new InvalidOperationException(
                        "Scheduler-enabled TickerQ registration requires an explicit application namespace.");
                return LegacyGlobal;
            }
            return new TickerQRuntimePartition(applicationNamespace);
        }

        private static string BuildStorageKey(ReadOnlySpan<byte> identity)
        {
            var framed = new byte[1 + 4 + identity.Length];
            framed[0] = 1;
            BinaryPrimitives.WriteInt32BigEndian(framed.AsSpan(1, 4), identity.Length);
            identity.CopyTo(framed.AsSpan(5));
            return StorageKeyPrefix + Convert.ToHexString(SHA256.HashData(framed)).ToLowerInvariant();
        }

        public bool Equals(TickerQRuntimePartition other)
            => other != null && StringComparer.Ordinal.Equals(StorageKey, other.StorageKey);

        public override bool Equals(object obj) => Equals(obj as TickerQRuntimePartition);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(StorageKey);
        public override string ToString() => ApplicationNamespace ?? "LegacyGlobal";
    }
}
