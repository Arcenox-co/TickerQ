using System.Text.Json;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class ExecutionLogDeltaTests
{
    private static TickerExecutionLogLine Line(Guid job, long time) => new() { TickerId = job, UnixMs = time, Message = "log", Category = "test", FunctionName = "test" };
    private static ExecutionLogReadResult Result(params TickerExecutionLogLine[] lines) => new(lines, false, true);

    [Fact]
    public void Unchanged_tail_sends_no_lines_and_append_sends_only_new_lines()
    {
        var id = Guid.NewGuid(); var first = Line(id, 1); var second = Line(id, 2);
        var initial = LogTailDelta.Create(id, Result(first), null!);
        Assert.True(initial.Reset);
        var same = LogTailDelta.Create(id, Result(first), initial.Cursor);
        Assert.False(same.Reset); Assert.Empty(same.Tail.Lines);
        var appended = LogTailDelta.Create(id, Result(first, second), same.Cursor);
        Assert.False(appended.Reset); Assert.Equal(second.Id, Assert.Single(appended.Tail.Lines).Id);
    }

    [Fact]
    public void Eviction_late_arrival_and_expiration_reset_the_client_tail()
    {
        var id = Guid.NewGuid(); var first = Line(id, 2); var second = Line(id, 3); var late = Line(id, 1);
        var initial = LogTailDelta.Create(id, Result(first), null!);
        var evicted = LogTailDelta.Create(id, Result(second), initial.Cursor);
        Assert.True(evicted.Reset); Assert.Equal(second.Id, Assert.Single(evicted.Tail.Lines).Id);
        var reordered = LogTailDelta.Create(id, Result(late, first), initial.Cursor);
        Assert.True(reordered.Reset); Assert.Equal(2, reordered.Tail.Lines.Count);
        var expired = LogTailDelta.Create(id, Result(), initial.Cursor);
        Assert.True(expired.Reset); Assert.Empty(expired.Tail.Lines);
        Assert.False(LogTailDelta.Create(id, Result(), expired.Cursor).Reset);
    }

    [Fact]
    public void Batch_contracts_serialize_with_generated_metadata()
    {
        var response = new TickerLogBatchResponse { Items = [LogTailDelta.Create(Guid.NewGuid(), Result(), null!)] };
        var json = JsonSerializer.Serialize(response, DashboardJsonSerializerContext.Default.TickerLogBatchResponse);
        Assert.Contains("cursor", json, StringComparison.OrdinalIgnoreCase);
        var request = JsonSerializer.Deserialize("{\"items\":[{\"tickerId\":\"" + response.Items[0].TickerId + "\"}]}",
            DashboardJsonSerializerContext.Default.TickerLogBatchRequest);
        Assert.Equal(response.Items[0].TickerId, Assert.Single(request!.Items).TickerId);
    }
}
