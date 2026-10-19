using System.IO.Compression;
using System.Text;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class ExecutionLogCodecMemoryTests
{
    [Fact]
    public void Tiny_archives_do_not_allocate_the_full_decompression_budget()
    {
        var id = Guid.NewGuid();
        var archive = ExecutionLogCodec.Encode(new ExecutionLogSnapshot(id, DateTime.UtcNow.Ticks,
            [new TickerExecutionLogLine { TickerId = id, Message = "tiny", Category = "test", FunctionName = "function" }], false),
            "test", TimeSpan.FromDays(1));
        ExecutionLogCodec.Decode(archive); // Warm gzip and the shared small buffer pool.
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++) ExecutionLogCodec.Decode(archive);
        var perDecode = (GC.GetAllocatedBytesForCurrentThread() - before) / 50;
        Assert.True(perDecode < 32 * 1024, $"A tiny decode allocated {perDecode} bytes.");
    }

    [Fact]
    public void Version_one_archives_still_decode()
    {
        var id = Guid.NewGuid();
        var archive = Archive(writer =>
        {
            writer.Write(1); writer.Write(1); writer.Write(id.ToByteArray());
            writer.Write(123L); writer.Write(2);
            WriteString(writer, "legacy"); WriteString(writer, "category"); WriteString(writer, "function");
            writer.Write(false);
        });
        var line = Assert.Single(ExecutionLogCodec.Decode(archive));
        Assert.Equal(id, line.Id);
        Assert.Equal("legacy", line.Message);
        Assert.Equal(TickerLifecycleEvent.None, line.LifecycleEvent);
    }

    [Fact]
    public void Maximum_valid_payload_decodes_but_trailing_bytes_are_rejected()
    {
        var archive = Archive(writer => WriteLine(writer, new string('x', ExecutionLogCodec.MaximumPayloadBytes - 55)));
        Assert.Equal(ExecutionLogCodec.MaximumPayloadBytes - 55, Assert.Single(ExecutionLogCodec.Decode(archive)).Message.Length);
        var excess = Archive(writer =>
        {
            WriteLine(writer, new string('x', ExecutionLogCodec.MaximumPayloadBytes - 55));
            writer.Write((byte)1);
        });
        Assert.Throws<InvalidDataException>(() => ExecutionLogCodec.Decode(excess));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(262144)]
    public void Invalid_string_lengths_are_rejected_without_large_allocations(int length)
    {
        var archive = Archive(writer =>
        {
            writer.Write(2); writer.Write(1); writer.Write(Guid.NewGuid().ToByteArray());
            writer.Write(123L); writer.Write(2); writer.Write(length);
        });
        Assert.Throws<InvalidDataException>(() => ExecutionLogCodec.Decode(archive));
    }

    [Fact]
    public void Truncated_text_invalid_metadata_and_trailing_data_are_rejected()
    {
        var truncated = Archive(writer =>
        {
            writer.Write(2); writer.Write(1); writer.Write(Guid.NewGuid().ToByteArray());
            writer.Write(123L); writer.Write(2); writer.Write(100); writer.Write((byte)1);
        });
        Assert.Throws<EndOfStreamException>(() => ExecutionLogCodec.Decode(truncated));
        var metadata = Archive(writer => WriteLine(writer, "text", lifecycle: 255));
        Assert.Throws<InvalidDataException>(() => ExecutionLogCodec.Decode(metadata));
        var trailing = Archive(writer => { WriteLine(writer, "text"); writer.Write(0); });
        Assert.Throws<InvalidDataException>(() => ExecutionLogCodec.Decode(trailing));
    }

    private static ExecutionLogArchive Archive(Action<BinaryWriter> write)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8)) write(writer);
        return new ExecutionLogArchive { TickerId = Guid.NewGuid(), Payload = output.ToArray() };
    }

    private static void WriteLine(BinaryWriter writer, string message, byte lifecycle = 0)
    {
        writer.Write(2); writer.Write(1); writer.Write(Guid.NewGuid().ToByteArray());
        writer.Write(123L); writer.Write(2);
        WriteString(writer, message); WriteString(writer, ""); WriteString(writer, "");
        writer.Write(false); writer.Write(lifecycle); writer.Write(byte.MaxValue); writer.Write(0);
    }

    private static void WriteString(BinaryWriter writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes.Length); writer.Write(bytes);
    }
}
