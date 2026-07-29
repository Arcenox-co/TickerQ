using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TickerQ.Utilities
{
    public static class TickerHelper
    {
        private static readonly byte[] GZipSignature = [0x1f, 0x8b, 0x08, 0x00];
        
        /// <summary>
        /// JsonSerializerOptions specifically for ticker request serialization/deserialization.
        /// Can be configured during application startup via TickerOptionsBuilder.
        /// </summary>
        public static JsonSerializerOptions RequestJsonSerializerOptions { get; set; } = new();

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "DefaultJsonTypeInfoResolver is created only when reflection serialization is enabled; Native AOT uses the configured source-generated resolver branch.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "DefaultJsonTypeInfoResolver is created only when reflection serialization is enabled; Native AOT uses the configured source-generated resolver branch.")]
        internal static JsonSerializerOptions GetEffectiveRequestJsonSerializerOptions()
        {
            var options = RequestJsonSerializerOptions ??= new JsonSerializerOptions();
            if (options.TypeInfoResolver != null)
                return options;

            if (!JsonSerializer.IsReflectionEnabledByDefault)
                throw new InvalidOperationException(
                    "Ticker request JsonTypeInfo is unavailable because reflection serialization is disabled. " +
                    "Configure AddTickerQ with WithJsonContext using a source-generated context that includes every request type.");

            // Do not mutate caller-owned options while Build is still staging registrations.
            return new JsonSerializerOptions(options)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
        }

        internal static void ValidateGeneratedSchemaSerializerProfile()
            => ValidateGeneratedSchemaSerializerProfile(RequestJsonSerializerOptions ?? new JsonSerializerOptions());

        internal static void ValidateGeneratedSchemaSerializerProfile(JsonSerializerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            string incompatible = null;

            if (options.PropertyNamingPolicy != null) incompatible = nameof(options.PropertyNamingPolicy);
            else if (options.DictionaryKeyPolicy != null) incompatible = nameof(options.DictionaryKeyPolicy);
            else if (options.IncludeFields) incompatible = nameof(options.IncludeFields);
            else if (options.DefaultIgnoreCondition != JsonIgnoreCondition.Never) incompatible = nameof(options.DefaultIgnoreCondition);
            else if (options.IgnoreReadOnlyProperties) incompatible = nameof(options.IgnoreReadOnlyProperties);
            else if (options.IgnoreReadOnlyFields) incompatible = nameof(options.IgnoreReadOnlyFields);
            else if (options.NumberHandling != JsonNumberHandling.Strict) incompatible = nameof(options.NumberHandling);
            else if (options.PropertyNameCaseInsensitive) incompatible = nameof(options.PropertyNameCaseInsensitive);
            else if (options.ReferenceHandler != null) incompatible = nameof(options.ReferenceHandler);
            else if (options.UnmappedMemberHandling != JsonUnmappedMemberHandling.Skip) incompatible = nameof(options.UnmappedMemberHandling);
            else if (options.Converters.Count != 0) incompatible = nameof(options.Converters);

            if (incompatible != null)
                throw new InvalidOperationException(
                    $"Ticker request JSON option '{incompatible}' changes the wire shape and is incompatible with compile-time generated request schemas. " +
                    "Use the default TickerQ request serializer profile or provide an explicit request contract through a non-generated registration path.");
        }
        
        /// <summary>
        /// Controls whether ticker requests are GZip-compressed.
        /// When false (default), requests are stored as plain UTF-8 JSON bytes without compression.
        /// </summary>
        public static bool UseGZipCompression { get; set; } = false;

        [RequiresUnreferencedCode("Legacy request serialization may use reflection metadata. Use the function-aware or JsonTypeInfo overload for trimming/AOT.")]
        [RequiresDynamicCode("Legacy request serialization may require runtime JSON metadata. Use the function-aware or JsonTypeInfo overload for Native AOT.")]
        public static byte[] CreateTickerRequest<T>(T data)
        {
            // If data is already a byte array, short-circuit where possible
            if (data is byte[] existingBytes)
            {
                // If compression is enabled and data already has the GZip signature, assume it is in the final format
                if (UseGZipCompression &&
                    existingBytes.Length >= GZipSignature.Length &&
                    existingBytes.TakeLast(GZipSignature.Length).SequenceEqual(GZipSignature))
                {
                    return existingBytes;
                }

                // If compression is disabled, treat the provided bytes as the final representation
                if (!UseGZipCompression)
                {
                    return existingBytes;
                }
            }

            var serialized = data is byte[] bytes
                ? bytes
                : JsonSerializer.SerializeToUtf8Bytes(data, RequestJsonSerializerOptions);

            if (!UseGZipCompression)
            {
                return serialized;
            }

            Span<byte> compressedBytes;
            using (var memoryStream = new MemoryStream())
            {
                using (var stream = new GZipStream(memoryStream, CompressionMode.Compress, true))
                {
                    stream.Write(serialized);
                }

                compressedBytes = memoryStream.GetBuffer().AsSpan()[..(int)memoryStream.Length];
            }

            var returnVal = new byte[compressedBytes.Length + GZipSignature.Length];
            var returnValSpan = returnVal.AsSpan();
            compressedBytes.CopyTo(returnValSpan);
            GZipSignature.AsSpan().CopyTo(returnValSpan[compressedBytes.Length..]);

            return returnVal;
        }

        /// <summary>
        /// Serializes with a function's registered runtime contract when available, falling
        /// back to the configured global options for registrations without runtime JSON metadata.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Registered source-generated functions return before the legacy fallback; missing metadata is an explicitly non-AOT legacy path.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Registered source-generated functions return before the legacy fallback; missing metadata is an explicitly non-AOT legacy path.")]
        public static byte[] CreateTickerRequest<T>(T data, string functionName)
        {
            return TickerFunctionProvider.TryGetRequestTypeInfo<T>(functionName, out var typeInfo)
                ? CreateTickerRequest(data, typeInfo)
                : CreateTickerRequest(data);
        }

        [RequiresUnreferencedCode("Legacy request deserialization may use reflection metadata. Use the JsonTypeInfo overload for trimming/AOT.")]
        [RequiresDynamicCode("Legacy request deserialization may require runtime JSON metadata. Use the JsonTypeInfo overload for Native AOT.")]
        public static T ReadTickerRequest<T>(byte[] gzipBytes)
        {
            if (gzipBytes == null || gzipBytes.Length == 0)
                return default;

            var serializedObject = ReadTickerRequestAsString(gzipBytes);

            if (string.IsNullOrWhiteSpace(serializedObject))
                return default;

            if (RequestJsonSerializerOptions?.TypeInfoResolver?.GetTypeInfo(typeof(T), RequestJsonSerializerOptions) is JsonTypeInfo<T> typeInfo)
                return JsonSerializer.Deserialize(serializedObject, typeInfo);

            return JsonSerializer.Deserialize<T>(serializedObject, RequestJsonSerializerOptions);
        }

        public static T ReadTickerRequest<T>(byte[] gzipBytes, JsonTypeInfo<T> typeInfo)
        {
            if (gzipBytes == null || gzipBytes.Length == 0)
                return default;

            var serializedObject = ReadTickerRequestAsString(gzipBytes);

            if (string.IsNullOrWhiteSpace(serializedObject))
                return default;

            return JsonSerializer.Deserialize(serializedObject, typeInfo);
        }

        public static byte[] CreateTickerRequest<T>(T data, JsonTypeInfo<T> typeInfo)
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(data, typeInfo);

            if (!UseGZipCompression)
                return serialized;

            Span<byte> compressedBytes;
            using (var memoryStream = new MemoryStream())
            {
                using (var stream = new GZipStream(memoryStream, CompressionMode.Compress, true))
                {
                    stream.Write(serialized);
                }

                compressedBytes = memoryStream.GetBuffer().AsSpan()[..(int)memoryStream.Length];
            }

            var returnVal = new byte[compressedBytes.Length + GZipSignature.Length];
            var returnValSpan = returnVal.AsSpan();
            compressedBytes.CopyTo(returnValSpan);
            GZipSignature.AsSpan().CopyTo(returnValSpan[compressedBytes.Length..]);

            return returnVal;
        }
        
        public static string ReadTickerRequestAsString(byte[] gzipBytes)
        {
            var isCompressed = gzipBytes.Length >= GZipSignature.Length &&
                               gzipBytes.TakeLast(GZipSignature.Length).SequenceEqual(GZipSignature);
            if (!isCompressed)
            {
                // Persisted rows can outlive compression configuration changes.
                return Encoding.UTF8.GetString(gzipBytes);
            }

            var compressedBytes = gzipBytes.Take(gzipBytes.Length - GZipSignature.Length).ToArray();

            using var memoryStream = new MemoryStream(compressedBytes);
            
            using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);

            using var streamReader = new StreamReader(gzipStream);

            var serializedObject = streamReader.ReadToEnd();

            return serializedObject;
        }
    }
}
