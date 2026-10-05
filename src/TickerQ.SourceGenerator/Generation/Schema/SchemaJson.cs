using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TickerQ.SourceGenerator.Generation.Schema
{
    /// <summary>
    /// Minimal, deterministic, reflection-free JSON writer used by the compile-time schema emitter.
    /// Object member keys are sorted ordinally so emitted output already matches the canonical form
    /// used by the runtime fingerprint (see TickerQ.Utilities.Serialization.JsonSchemaCanonicalizer),
    /// while arrays preserve element order.
    /// </summary>
    internal static class SchemaJson
    {
        public static string String(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Serializes an object from ordered members, sorting keys ordinally for determinism.</summary>
        public static string Object(IEnumerable<KeyValuePair<string, string>> members)
        {
            var list = new List<KeyValuePair<string, string>>(members);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var sb = new StringBuilder();
            sb.Append('{');
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(String(list[i].Key)).Append(':').Append(list[i].Value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Serializes an array from raw JSON element strings, preserving order.</summary>
        public static string Array(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            var first = true;
            foreach (var item in items)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(item);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
