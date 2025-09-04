﻿using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace TickerQ.Utilities
{
    public static class TickerHelper
    {
        private static readonly byte[] GZipSignature = { 0x1f, 0x8b, 0x08, 0x00 };

        public static byte[] CreateTickerRequest<T>(T data)
        {
            Span<byte> compressedBytes;
            var serialized = JsonSerializer.SerializeToUtf8Bytes(data);
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (GZipStream stream = new GZipStream(memoryStream, CompressionMode.Compress, true))
                {
                    stream.Write(serialized);
                }

                compressedBytes = memoryStream.GetBuffer().AsSpan()[..(int)memoryStream.Length];
            }

            var returnVal = new byte[compressedBytes.Length + GZipSignature.Length];
            var returnValSpan = returnVal.AsSpan();
            compressedBytes.CopyTo(returnValSpan);
            GZipSignature.AsSpan().CopyTo(returnValSpan.Slice(compressedBytes.Length));

            return returnVal;
        }

        public static T ReadTickerRequest<T>(byte[] gzipBytes)
        {
            var serializedObject = ReadTickerRequestAsString(gzipBytes);
            
            return JsonSerializer.Deserialize<T>(serializedObject);
        }
        
        public static string ReadTickerRequestAsString(byte[] gzipBytes)
        {
            if (!gzipBytes.Skip(gzipBytes.Length - GZipSignature.Length).SequenceEqual(GZipSignature))
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
