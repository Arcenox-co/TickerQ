using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

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
        
        /// <summary>
        /// Controls whether ticker requests are GZip-compressed.
        /// When false (default), requests are stored as plain UTF-8 JSON bytes without compression.
        /// </summary>
        public static bool UseGZipCompression { get; set; } = false;

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

        public static T ReadTickerRequest<T>(byte[] gzipBytes)
        {
            var serializedObject = ReadTickerRequestAsString(gzipBytes);
            
            return JsonSerializer.Deserialize<T>(serializedObject, RequestJsonSerializerOptions);
        }
        
        public static string ReadTickerRequestAsString(byte[] gzipBytes)
        {
            if (!UseGZipCompression)
            {
                // When compression is disabled, treat the bytes as plain UTF-8 JSON
                return Encoding.UTF8.GetString(gzipBytes);
            }

            if (!gzipBytes.TakeLast(GZipSignature.Length).SequenceEqual(GZipSignature))
            {
                throw new Exception("The bytes are not GZip compressed.");
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
