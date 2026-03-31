using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TickerQ.Dashboard.Infrastructure.Dashboard
{
    /// <summary>
    /// Generates example JSON using JsonTypeInfo.Properties — no reflection.
    /// Uses the configured JsonSerializerOptions which includes source-gen contexts.
    /// Results are cached per type.
    /// </summary>
    internal static class JsonExampleGenerator
    {
        private static readonly ConcurrentDictionary<Type, string> Cache = new();
        private static JsonSerializerOptions _options;

        internal static void Configure(JsonSerializerOptions options)
        {
            _options = options;
        }

        public static bool TryGenerateExampleJson(Type type, out string json)
        {
            if (_options == null)
            {
                json = string.Empty;
                return false;
            }

            if (Cache.TryGetValue(type, out json))
                return json.Length > 0;

            try
            {
                var typeInfo = _options.GetTypeInfo(type);
                json = BuildExampleJson(typeInfo, depth: 0);
                Cache[type] = json;
                return true;
            }
            catch (Exception)
            {
                json = string.Empty;
                Cache[type] = json;
                return false;
            }
        }

        private static string BuildExampleJson(JsonTypeInfo typeInfo, int depth, HashSet<Type> visited = null)
        {
            if (depth > 2)
                return "null";

            // Primitives / known value types — use the serializer to produce the value
            var primitiveExample = GetPrimitiveExample(typeInfo.Type);
            if (primitiveExample != null)
                return primitiveExample;

            // Enum
            if (typeInfo.Type.IsEnum)
                return "0";

            // Object with properties
            if (typeInfo.Kind == JsonTypeInfoKind.Object)
            {
                visited ??= new HashSet<Type>();
                if (!visited.Add(typeInfo.Type))
                    return "null"; // Circular reference

                var parts = new List<string>();
                foreach (var prop in typeInfo.Properties)
                {
                    if (prop.Get == null) continue; // Write-only — skip

                    string propValue;
                    try
                    {
                        var propTypeInfo = _options.GetTypeInfo(prop.PropertyType);
                        propValue = BuildExampleJson(propTypeInfo, depth + 1, visited);
                    }
                    catch
                    {
                        propValue = "null";
                    }

                    // prop.Name is already the serialized name (respects [JsonPropertyName], naming policy)
                    parts.Add($"\"{prop.Name}\": {propValue}");
                }

                visited.Remove(typeInfo.Type);
                return "{" + string.Join(", ", parts) + "}";
            }

            // Enumerable (array, list, etc.)
            if (typeInfo.Kind == JsonTypeInfoKind.Enumerable)
            {
                // Try to get element type and build one example element
                var elementType = GetEnumerableElementType(typeInfo.Type);
                if (elementType != null)
                {
                    try
                    {
                        var elementTypeInfo = _options.GetTypeInfo(elementType);
                        var elementExample = BuildExampleJson(elementTypeInfo, depth + 1, visited);
                        return $"[{elementExample}]";
                    }
                    catch { }
                }
                return "[]";
            }

            // Dictionary
            if (typeInfo.Kind == JsonTypeInfoKind.Dictionary)
            {
                return "{}";
            }

            // Fallback: try CreateObject + Serialize
            if (typeInfo.CreateObject != null)
            {
                try
                {
                    var instance = typeInfo.CreateObject();
                    return JsonSerializer.Serialize(instance, typeInfo);
                }
                catch { }
            }

            return "null";
        }

        private static string GetPrimitiveExample(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            var t = underlying ?? type;

            if (t == typeof(string)) return "\"string\"";
            if (t == typeof(bool)) return "true";
            if (t == typeof(int) || t == typeof(short) || t == typeof(byte) ||
                t == typeof(uint) || t == typeof(ushort) || t == typeof(sbyte)) return "123";
            if (t == typeof(long) || t == typeof(ulong)) return "123";
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return "123.45";
            if (t == typeof(char)) return "\"a\"";
            if (t == typeof(Guid)) return "\"01234567-89ab-cdef-0123-456789abcdef\"";
            if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return "\"2023-01-01T00:00:00Z\"";
            if (t == typeof(TimeSpan)) return "\"00:30:00\"";
            if (t == typeof(byte[])) return "\"AQID\"";

            return null;
        }

        private static Type GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                if (args.Length == 1)
                    return args[0];
            }

            return null;
        }
    }
}
