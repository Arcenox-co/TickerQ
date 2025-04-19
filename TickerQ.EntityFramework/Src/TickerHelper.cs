using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace TickerQ.EntityFrameworkCore
{
    public static class TickerHelper
    {
        private static readonly byte[] GZipSignature = { 0x1f, 0x8b, 0x08, 0x00 };

        public static byte[] CreateTickerRequest<T>(T data)
        {
            var serializedData = JsonSerializer.Serialize(data);

            byte[] compressedBytes;

            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                {
                    using var streamWriter = new StreamWriter(gzipStream);

                    streamWriter.Write(serializedData);
                }
                compressedBytes = memoryStream.ToArray();
            }

            return compressedBytes.Concat(GZipSignature).ToArray();
        }

        public static T ReadTickerRequest<T>(byte[] gzipBytes)
        {
            var serializedObject = ReadTickerRequestAsString(gzipBytes);
            
            return JsonSerializer.Deserialize<T>(serializedObject);
        }
        
        public static string ReadTickerRequestAsString(byte[] gzipBytes)
        {
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
