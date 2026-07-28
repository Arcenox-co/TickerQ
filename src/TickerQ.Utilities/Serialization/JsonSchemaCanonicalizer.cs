using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TickerQ.Utilities.Serialization
{
    /// <summary>
    /// Produces a deterministic, compact UTF-8 canonical form of a JSON Schema object so that two
    /// semantically identical schemas hash identically, and — critically — so that the .NET and Node
    /// SDKs emit <b>byte-for-byte identical</b> canonical bytes for the same schema (transport blocker 1).
    /// </summary>
    /// <remarks>
    /// <para>Canonical format (implemented identically by the Node SDK's
    /// <c>hub/sdks/node/src/infrastructure/CanonicalContract.ts</c>):</para>
    /// <list type="bullet">
    ///   <item>Object property names are sorted with <b>ordinal</b> (UTF-16 code-unit) ordering, recursively.</item>
    ///   <item>Array element order is preserved verbatim (arrays are never sorted).</item>
    ///   <item>Duplicate object keys are rejected.</item>
    ///   <item>Nesting deeper than <see cref="MaxDepth"/> is rejected.</item>
    ///   <item><b>Strings</b> are escaped exactly as ECMA-262 <c>JSON.stringify</c>: <c>\" \\ \b \t \n \f \r</c>;
    ///     other C0 controls (U+0000–U+001F) as lowercase <c>\u00xx</c>; every other code point — including
    ///     non-ASCII such as <c>é</c> and non-BMP such as <c>😀</c> — is emitted as raw UTF-8. Unpaired
    ///     surrogates cannot be represented losslessly and are rejected.</item>
    ///   <item><b>Numbers</b> are normalized by value: only integer-valued literals within the JS
    ///     safe-integer range <c>[-(2^53-1), 2^53-1]</c> are accepted (<c>1.0</c>→<c>1</c>, <c>1e2</c>→<c>100</c>,
    ///     <c>-0</c>→<c>0</c>). Fractions, non-integer exponents, and magnitudes beyond the safe range are
    ///     <b>rejected</b> — Node's <c>JSON.parse</c> cannot reproduce their exact bytes, so hashing them
    ///     would silently diverge across runtimes.</item>
    /// </list>
    /// <para>Fully reflection-free and AOT/trimming-safe.</para>
    /// </remarks>
    internal static class JsonSchemaCanonicalizer
    {
        /// <summary>
        /// Maximum canonicalization nesting depth. Matches the <see cref="JsonDocument"/> default so a
        /// schema that parses at default limits always canonicalizes, while elements constructed under
        /// relaxed parse options are still bounded here.
        /// </summary>
        internal const int MaxDepth = 64;

        /// <summary>Largest exactly-representable JS integer, <c>2^53 - 1</c> (Number.MAX_SAFE_INTEGER).</summary>
        internal const long MaxSafeInteger = 9007199254740991L;

        /// <summary>
        /// Canonicalizes an object-root schema into deterministic compact UTF-8 bytes.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The root is not a JSON object, contains duplicate object keys, exceeds <see cref="MaxDepth"/>,
        /// contains an unpaired surrogate, or contains a numeric literal that cannot be represented
        /// identically across runtimes.
        /// </exception>
        public static byte[] CanonicalizeToUtf8(JsonElement schema)
        {
            var sb = new StringBuilder(256);
            AppendCanonical(sb, schema);
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>Canonicalizes an object-root schema into a deterministic compact UTF-8 string.</summary>
        public static string Canonicalize(JsonElement schema)
        {
            var sb = new StringBuilder(256);
            AppendCanonical(sb, schema);
            return sb.ToString();
        }

        /// <summary>Appends the canonical form of an object-root schema (the public entry contract).</summary>
        private static void AppendCanonical(StringBuilder sb, JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object)
                throw new ArgumentException(
                    $"Schema root must be a JSON object (root was {schema.ValueKind}).", nameof(schema));
            Write(sb, schema, depth: 0);
        }

        private static void Write(StringBuilder sb, JsonElement element, int depth)
        {
            if (depth > MaxDepth)
                throw new ArgumentException(
                    $"Schema nesting exceeds the maximum canonicalization depth of {MaxDepth}.", "schema");

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    sb.Append('{');
                    WriteObjectMembers(sb, element, depth);
                    sb.Append('}');
                    break;

                case JsonValueKind.Array:
                    sb.Append('[');
                    var firstItem = true;
                    foreach (var item in element.EnumerateArray())
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        Write(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    break;

                case JsonValueKind.String:
                    AppendCanonicalString(sb, ReadString(element));
                    break;

                case JsonValueKind.Number:
                    AppendCanonicalNumber(sb, element);
                    break;

                case JsonValueKind.True:
                    sb.Append("true");
                    break;

                case JsonValueKind.False:
                    sb.Append("false");
                    break;

                case JsonValueKind.Null:
                    sb.Append("null");
                    break;

                default:
                    throw new ArgumentException($"Unsupported JSON value kind {element.ValueKind}.", "schema");
            }
        }

        private static void WriteObjectMembers(StringBuilder sb, JsonElement obj, int depth)
        {
            // Ordinal (UTF-16 code-unit) sort of property names, matching JS Array.prototype.sort on keys,
            // then reject adjacent duplicates.
            var properties = new List<JsonProperty>();
            foreach (var property in obj.EnumerateObject())
                properties.Add(property);

            properties.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

            for (var i = 0; i < properties.Count; i++)
            {
                if (i > 0 && string.Equals(properties[i].Name, properties[i - 1].Name, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Schema contains duplicate object key '{properties[i].Name}'.", "schema");

                if (i > 0) sb.Append(',');
                AppendCanonicalString(sb, properties[i].Name);
                sb.Append(':');
                Write(sb, properties[i].Value, depth + 1);
            }
        }

        private static string ReadString(JsonElement element)
        {
            try
            {
                return element.GetString();
            }
            catch (InvalidOperationException)
            {
                // System.Text.Json throws when a string contains an unpaired surrogate that cannot be
                // decoded to valid UTF-16. Surface as a canonicalization rejection.
                throw new ArgumentException(
                    "Schema string contains an unpaired surrogate and cannot be canonicalized.", "schema");
            }
        }

        /// <summary>
        /// Appends a JSON string literal escaped exactly as ECMA-262 <c>JSON.stringify</c> so the bytes
        /// match the Node SDK. Reused by <see cref="TickerRequestContractFingerprint"/> for the media type.
        /// </summary>
        internal static void AppendCanonicalString(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < 0x20)
                        {
                            AppendUnicodeEscape(sb, c);
                        }
                        else if (char.IsHighSurrogate(c))
                        {
                            if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                            {
                                // Valid non-BMP pair: emit both code units raw; UTF-8 encoding yields the
                                // correct 4-byte sequence, matching JSON.stringify.
                                sb.Append(c);
                                sb.Append(value[i + 1]);
                                i++;
                            }
                            else
                            {
                                // Unpaired high surrogate — cannot be represented as valid UTF-8; JSON.stringify
                                // emits a lowercase \udxxx escape, so match it rather than corrupt to U+FFFD.
                                AppendUnicodeEscape(sb, c);
                            }
                        }
                        else if (char.IsLowSurrogate(c))
                        {
                            // Unpaired low surrogate (a paired one is consumed above).
                            AppendUnicodeEscape(sb, c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void AppendUnicodeEscape(StringBuilder sb, char c)
        {
            sb.Append("\\u");
            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Appends a canonical JSON number. Only integer-valued literals within the JS safe-integer range
        /// are accepted; everything else is rejected (see the class remarks).
        /// </summary>
        internal static void AppendCanonicalNumber(StringBuilder sb, JsonElement element)
        {
            var raw = element.GetRawText();
            if (!element.TryGetDecimal(out var value))
                throw new ArgumentException(
                    $"Schema numeric literal '{raw}' cannot be represented identically across runtimes " +
                    "(magnitude beyond a JS-safe integer). Express integer-valued numbers within " +
                    "±(2^53-1), or omit the constraint.", "schema");

            if (Math.Truncate(value) != value)
                throw new ArgumentException(
                    $"Schema numeric literal '{raw}' is not integer-valued. Node's JSON.parse cannot reproduce " +
                    "fractional/exponent literals byte-for-byte, so they are rejected. Use an integer or omit " +
                    "the constraint.", "schema");

            if (value < -MaxSafeInteger || value > MaxSafeInteger)
                throw new ArgumentException(
                    $"Schema numeric literal '{raw}' exceeds the JS safe-integer range ±(2^53-1) and cannot be " +
                    "represented identically across runtimes.", "schema");

            sb.Append(((long)value).ToString(CultureInfo.InvariantCulture));
        }
    }
}
