using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

internal sealed record ExecutionLogSnapshot(Guid TickerId, long Version, IReadOnlyList<TickerExecutionLogLine> Lines, bool Truncated);

internal static class ExecutionLogCodec
{
    internal const int MaximumPayloadBytes = 262144;
    internal static int LineSize(TickerExecutionLogLine line) => 47 + Encoding.UTF8.GetByteCount(line.Message)
        + Encoding.UTF8.GetByteCount(line.Category) + Encoding.UTF8.GetByteCount(line.FunctionName);

    public static ExecutionLogArchive Encode(ExecutionLogSnapshot snapshot, string partition, TimeSpan retention)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(2); writer.Write(snapshot.Lines.Count);
            foreach (var line in snapshot.Lines)
            {
                writer.Write(line.Id.ToByteArray()); writer.Write(line.UnixMs); writer.Write(line.Level);
                WriteString(writer, line.Message); WriteString(writer, line.Category); WriteString(writer, line.FunctionName);
                writer.Write(line.Truncated);
                writer.Write((byte)line.LifecycleEvent);
                writer.Write(line.Status.HasValue ? (byte)line.Status.Value : byte.MaxValue);
                writer.Write(line.Attempt);
            }
        }
        return new ExecutionLogArchive
        {
            Id = ExecutionLogArchive.StorageId(partition, snapshot.TickerId), PartitionKey = partition,
            TickerId = snapshot.TickerId, UpdatedAtUtcTicks = snapshot.Version,
            ExpiresAtUtcTicks = checked(snapshot.Version + retention.Ticks), Payload = output.ToArray(),
            Truncated = snapshot.Truncated
        };
    }

    public static IReadOnlyList<TickerExecutionLogLine> Decode(ExecutionLogArchive record)
    {
        if (record.Payload == null || record.Payload.Length > MaximumPayloadBytes + 1024)
            throw new InvalidDataException("Invalid log payload size.");
        using var input = new MemoryStream(record.Payload, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        var bytes = new byte[MaximumPayloadBytes + 1];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = gzip.Read(bytes, total, bytes.Length - total);
            if (read == 0) break;
            total += read;
        }
        if (total > MaximumPayloadBytes) throw new InvalidDataException("Log payload exceeds the decompression limit.");
        using var stream = new MemoryStream(bytes, 0, total, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var version = reader.ReadInt32();
        if (version is not (1 or 2)) throw new InvalidDataException("Unsupported log payload version.");
        var count = reader.ReadInt32();
        if (count < 0 || count > 1000) throw new InvalidDataException("Invalid log line count.");
        var lines = new List<TickerExecutionLogLine>(count);
        for (var i = 0; i < count; i++)
        {
            var line = new TickerExecutionLogLine
            {
                Id = new Guid(reader.ReadBytes(16)), TickerId = record.TickerId,
                UnixMs = reader.ReadInt64(), Level = reader.ReadInt32(),
                Message = ReadString(reader), Category = ReadString(reader), FunctionName = ReadString(reader),
                Truncated = reader.ReadBoolean()
            };
            if (version >= 2)
            {
                line.LifecycleEvent = (TickerLifecycleEvent)reader.ReadByte();
                var status = reader.ReadByte();
                line.Status = status == byte.MaxValue ? null : (TickerQ.Utilities.Enums.TickerStatus)status;
                line.Attempt = reader.ReadInt32();
                if (!Enum.IsDefined(line.LifecycleEvent) || (line.Status.HasValue && !Enum.IsDefined(line.Status.Value)) || line.Attempt < 0)
                    throw new InvalidDataException("Invalid lifecycle metadata.");
            }
            lines.Add(line);
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Invalid log payload trailing bytes.");
        return lines;
    }

    private static void WriteString(BinaryWriter writer, string text)
    { var bytes = Encoding.UTF8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid log text length.");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }
}
