using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Serialization
{
    /// <summary>
    /// Computes deterministic request-contract fingerprints for contract-evolution detection
    /// (typed-request-contracts plan, Task 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fingerprint is <c>"sha256:"</c> followed by the lowercase hex SHA-256 of a deterministic
    /// UTF-8 pre-image that frames the contract version, media type, requiredness, and the
    /// <b>canonical</b> schema. Two
    /// contracts whose schemas differ only by property order or whitespace produce the same
    /// fingerprint; a change to the schema (including reordering a JSON array such as <c>required</c>,
    /// which is never sorted), the contract version, media type, or requiredness produces a different fingerprint.
    /// </para>
    /// <para>Reflection-free and AOT/trimming-safe.</para>
    /// </remarks>
    internal static class TickerRequestContractFingerprint
    {
        /// <summary>Algorithm prefix carried on every fingerprint.</summary>
        public const string Prefix = "sha256:";

        /// <summary>
        /// Computes a fingerprint from already-canonical schema bytes plus the wire media type and
        /// requiredness. The caller is responsible for supplying canonical bytes (see
        /// <see cref="JsonSchemaCanonicalizer"/>).
        /// </summary>
        public static string Compute(
            ReadOnlySpan<byte> canonicalSchemaUtf8,
            string mediaType,
            bool required,
            int contractVersion = TickerRequestContractConstants.InitialContractVersion)
        {
            if (contractVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(contractVersion), contractVersion,
                    "Contract version must be positive.");

            // Deterministic, unambiguous pre-image with a FIXED key order (contractVersion, mediaType,
            // required, schema — which also happens to be ordinal order). A fixed-key JSON envelope frames
            // each component so that, e.g., mediaType "a" + schema X cannot collide with mediaType "" +
            // schema aX. The envelope is assembled with the SAME canonical string escaping and the raw
            // canonical schema bytes so the Node SDK produces byte-identical pre-images (transport blocker 1).
            var sb = new StringBuilder(canonicalSchemaUtf8.Length + 96);
            sb.Append("{\"contractVersion\":");
            sb.Append(contractVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"mediaType\":");
            JsonSchemaCanonicalizer.AppendCanonicalString(sb, mediaType);
            sb.Append(",\"required\":");
            sb.Append(required ? "true" : "false");
            sb.Append(",\"schema\":");
            // The canonical schema bytes are already valid, escaped-canonical UTF-8; embed verbatim.
            sb.Append(Encoding.UTF8.GetString(canonicalSchemaUtf8));
            sb.Append('}');

            var preimage = Encoding.UTF8.GetBytes(sb.ToString());

            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(preimage, hash);
            return string.Concat(Prefix, Convert.ToHexStringLower(hash));
        }

        /// <summary>
        /// Canonicalizes <paramref name="schema"/> and computes its fingerprint. Convenience for
        /// callers holding a raw (possibly non-canonical) schema element.
        /// </summary>
        public static string ComputeFromSchema(
            JsonElement schema,
            string mediaType,
            bool required,
            int contractVersion = TickerRequestContractConstants.InitialContractVersion)
            => Compute(JsonSchemaCanonicalizer.CanonicalizeToUtf8(schema), mediaType, required, contractVersion);
    }
}
