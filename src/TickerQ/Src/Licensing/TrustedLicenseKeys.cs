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
        // IMPORTANT: Production signing key ids MUST be pinned here (with their raw Ed25519 public keys,
        // Base64-encoded) before release. Only the development key is pinned today. Do NOT add a
        // configuration-driven or certificate-driven key lookup, and do NOT add a fail-open override:
        // an unknown key id must always fail closed.
        private static readonly IReadOnlyDictionary<string, byte[]> Keys = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["arcenox-development-1"] = Convert.FromBase64String("eOED5K08EVX5dUqa3vgSqb03AGugZ+vpWUO+EaQSTEs="),
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
