using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace TickerQ.EntityFrameworkCore.Src
{
    public class TickerHelper
    {
        public static readonly byte[] GZipSignature = new byte[] { 0x1f, 0x8b, 0x08, 0x00 };

        public static byte[] CreateTickerRequest<T>(T data)
        {
            string serializedData = JsonSerializer.Serialize(data);

            byte[] compressedBytes;

            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                {
                    using StreamWriter streamWriter = new StreamWriter(gzipStream);

                    streamWriter.Write(serializedData);
                }
                compressedBytes = memoryStream.ToArray();
            }

            return compressedBytes.Concat(GZipSignature).ToArray();
        }

        public static T ReadTickerRequest<T>(byte[] gzipBytes)
        {
            if (!gzipBytes.TakeLast(GZipSignature.Length).SequenceEqual(GZipSignature))
            {
                throw new Exception("The bytes are not GZip compressed.");
            }

            byte[] compressedBytes = gzipBytes.Take(gzipBytes.Length - GZipSignature.Length).ToArray();

            string serializedObject;

            using (MemoryStream memoryStream = new MemoryStream(compressedBytes))
            {
                using GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);

                using StreamReader streamReader = new StreamReader(gzipStream);

                serializedObject = streamReader.ReadToEnd();
            }

            return JsonSerializer.Deserialize<T>(serializedObject);
        }
    }
}
