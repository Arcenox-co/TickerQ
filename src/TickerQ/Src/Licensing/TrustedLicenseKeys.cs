using System;
using System.Collections.Generic;

namespace TickerQ.Licensing
{
    /// <summary>
    /// The immutable, source-pinned registry of trusted Ed25519 signing keys. A certificate is only
    /// verified against a key that is pinned here, keyed by its envelope key id. The public key is NEVER
    /// taken from the certificate itself or from configuration — trusting a certificate-provided or
    /// operator-provided key would defeat offline signature verification entirely.
    /// </summary>
    internal static class TrustedLicenseKeys
    {
        // Production signing keys are retained here for the lifetime of certificates they signed. Development
        // authorities must never enter this release trust store. Unknown key ids always fail closed.
        private static readonly IReadOnlyDictionary<string, byte[]> Keys = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["production-2026-01"] = Convert.FromBase64String("1vSynYg0x6OFd/6/qb7ug5ERbZcTwHtz68pIO1vNQ14="),
        };

        /// <summary>
        /// Returns the pinned raw public key for the given key id, or null when the key id is not trusted.
        /// A null result must be treated as a verification failure (fail closed).
        /// </summary>
        public static byte[] TryGetPublicKey(string keyId)
        {
            if (string.IsNullOrEmpty(keyId))
                return null;
            return Keys.TryGetValue(keyId, out var publicKey) ? publicKey : null;
        }
    }
}
