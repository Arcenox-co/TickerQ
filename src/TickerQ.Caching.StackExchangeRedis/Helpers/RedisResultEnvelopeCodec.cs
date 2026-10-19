#nullable disable
using System.Buffers.Binary;
using System.Text;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

/// <summary>Strict versioned binary format for immutable result envelopes stored beside Redis entities.</summary>
internal static class RedisResultEnvelopeCodec
{
    internal const int MaxPayloadBytes = 1024 * 1024;
    private const int MaxMetadataBytes = 64 * 1024;
    private const byte StorageVersion = 1;
    private static ReadOnlySpan<byte> Magic => "TQRE"u8;
    private const int HeaderLength = 25;
    private const int MaxStoredBytes = HeaderLength + MaxPayloadBytes + (3 * MaxMetadataBytes);

    internal static byte[] Serialize(TickerResultEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        envelope.EnsureSupportedVersion();
        if (envelope.PayloadLength > MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(envelope), envelope.PayloadLength,
                $"Ticker result payload exceeds the {MaxPayloadBytes}-byte limit.");

        var mediaType = EncodeRequired(envelope.MediaType, nameof(envelope.MediaType));
        var contractId = EncodeOptional(envelope.ContractId, nameof(envelope.ContractId));
        var contractType = EncodeOptional(envelope.ContractType, nameof(envelope.ContractType));
        var payload = envelope.ToPayloadArray();
        var result = new byte[HeaderLength + mediaType.Length +
            (contractId?.Length ?? 0) + (contractType?.Length ?? 0) + payload.Length];
        Magic.CopyTo(result);
        result[4] = StorageVersion;
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(5, 4), envelope.Version);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(9, 4), mediaType.Length);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(13, 4), contractId?.Length ?? -1);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(17, 4), contractType?.Length ?? -1);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(21, 4), payload.Length);
        var offset = HeaderLength;
        Copy(mediaType, result, ref offset);
        if (contractId is not null) Copy(contractId, result, ref offset);
        if (contractType is not null) Copy(contractType, result, ref offset);
        Copy(payload, result, ref offset);
        return result;
    }

    internal static TickerResultEnvelope TryDeserialize(byte[] value)
    {
        if (value is null || value.Length < HeaderLength || value.Length > MaxStoredBytes)
            return null;
        var span = value.AsSpan();
        if (!span[..4].SequenceEqual(Magic) || span[4] != StorageVersion)
            return null;

        var version = BinaryPrimitives.ReadInt32BigEndian(span.Slice(5, 4));
        var mediaLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(9, 4));
        var contractIdLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(13, 4));
        var contractTypeLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(17, 4));
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(21, 4));
        if (version != TickerResultEnvelope.CurrentVersion ||
            mediaLength <= 0 || mediaLength > MaxMetadataBytes ||
            !ValidOptionalLength(contractIdLength) || !ValidOptionalLength(contractTypeLength) ||
            payloadLength < 0 || payloadLength > MaxPayloadBytes)
            return null;

        var expected = (long)HeaderLength + mediaLength + Math.Max(contractIdLength, 0) +
            Math.Max(contractTypeLength, 0) + payloadLength;
        if (expected != value.Length)
            return null;

        try
        {
            var offset = HeaderLength;
            var mediaType = Decode(span, ref offset, mediaLength);
            var contractId = contractIdLength < 0 ? null : Decode(span, ref offset, contractIdLength);
            var contractType = contractTypeLength < 0 ? null : Decode(span, ref offset, contractTypeLength);
            var payload = span.Slice(offset, payloadLength).ToArray();
            if (string.IsNullOrWhiteSpace(mediaType) ||
                contractId is not null && string.IsNullOrWhiteSpace(contractId) ||
                contractType is not null && string.IsNullOrWhiteSpace(contractType))
                return null;
            return new TickerResultEnvelope(payload, version, mediaType, contractId, contractType);
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool ValidOptionalLength(int length) => length == -1 || length is >= 1 and <= MaxMetadataBytes;

    private static byte[] EncodeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value must be non-blank.", name);
        return Encode(value, name);
    }

    private static byte[] EncodeOptional(string value, string name) => value is null ? null : EncodeRequired(value, name);

    private static byte[] Encode(string value, string name)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(value);
        if (bytes.Length > MaxMetadataBytes)
            throw new ArgumentOutOfRangeException(name, bytes.Length, $"Metadata exceeds {MaxMetadataBytes} UTF-8 bytes.");
        return bytes;
    }

    private static string Decode(ReadOnlySpan<byte> span, ref int offset, int length)
    {
        var result = new UTF8Encoding(false, true).GetString(span.Slice(offset, length));
        offset += length;
        return result;
    }

    private static void Copy(byte[] source, byte[] destination, ref int offset)
    {
        source.CopyTo(destination, offset);
        offset += source.Length;
    }
}
