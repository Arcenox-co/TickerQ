using TickerQ.SDK.Logging;
using TickerQ.Worker.V1;
using Xunit;

namespace TickerQ.SDK.Tests;

public sealed class ExecutionLogMemoryTests
{
    [Fact]
    public void Queue_caps_message_and_metadata_before_retaining_and_counts_oldest_drops()
    {
        var queue = new TickerExecutionLogQueue();
        var line = new LogLine
        {
            Message = new string('x', 100_000), Category = new string('c', 10_000),
            FunctionName = new string('f', 10_000), Source = new string('s', 10_000)
        };
        for (var i = 0; i < TickerExecutionLogQueue.Capacity + 7; i++)
        {
            line.UnixMs = i;
            queue.TryEnqueue(line);
        }
        Assert.Equal(7, queue.ConsumeDroppedCount());
        Assert.Equal(0, queue.ConsumeDroppedCount());
        line.Message = "caller mutation";
        var lines = new List<LogLine>();
        while (queue.Reader.TryRead(out var retained)) lines.Add(retained);
        Assert.Equal(TickerExecutionLogQueue.Capacity, lines.Count);
        Assert.Equal(7, lines[0].UnixMs);
        Assert.All(lines, retained =>
        {
            Assert.Equal(TickerExecutionLogQueue.MaximumMessageCharacters, retained.Message.Length);
            Assert.Equal(TickerExecutionLogQueue.MaximumMetadataCharacters, retained.Category.Length);
            Assert.Equal(TickerExecutionLogQueue.MaximumMetadataCharacters, retained.FunctionName.Length);
            Assert.Equal(TickerExecutionLogQueue.MaximumMetadataCharacters, retained.Source.Length);
        });
    }
}
