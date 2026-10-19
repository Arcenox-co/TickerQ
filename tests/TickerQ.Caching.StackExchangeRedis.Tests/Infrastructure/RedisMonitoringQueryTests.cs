using System.Text;
using System.Text.Json;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

public sealed class RedisMonitoringReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MonitoringReader_SkipsPayloadsAndCustomFields_AndVisitsChildrenBeforeParent(bool camelCase)
    {
        var id = Guid.NewGuid();
        var child = Guid.NewGuid();
        var generation = Guid.NewGuid();
        // These payload types deliberately cannot deserialize as TimeTickerEntity.
        var json = $$$"""
            {"Children":[{"Id":"{{{child}}}","Function":"monitor","Status":2,"Children":{}}],
             "Id":"{{{id}}}","Function":"monitor","Status":3,"ExecutedAt":"2026-10-18T12:00:00Z",
             "ElapsedTime":1234,"ChainGeneration":"{{{generation}}}","AcquisitionToken":null,
             "Request":"not base64!","ExceptionMessage":{"large":"unused"},"Custom":{"Children":[1,2,3]}}
            """;
        if (camelCase)
            foreach (var property in new[] { "Children", "Id", "Function", "Status", "ExecutedAt", "ElapsedTime", "ChainGeneration", "AcquisitionToken" })
                json = json.Replace($"\"{property}\"", $"\"{char.ToLowerInvariant(property[0])}{property[1..]}\"");
        var rows = new List<RedisMonitoringMetadata>();
        RedisMonitoringReader.Visit(Encoding.UTF8.GetBytes(json), "monitor", true, rows.Add, default);
        Assert.Equal(new[] { child, id }, rows.Select(x => x.Id));
        Assert.All(rows, row => Assert.True(row.FunctionMatches));
        Assert.Equal(1234, rows[1].Execution(false).ElapsedMilliseconds);
        Assert.Equal(generation, rows[1].Execution(false).Generation);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}{}")]
    [InlineData("{\"Children\":[123]}")]
    public void MonitoringReader_RejectsMalformedRecords(string json)
        => Assert.ThrowsAny<JsonException>(() => RedisMonitoringReader.Visit(Encoding.UTF8.GetBytes(json), "monitor", true, _ => { }, default));

    [Fact]
    public void MonitoringReader_ObservesCancellation()
        => Assert.Throws<OperationCanceledException>(() =>
            RedisMonitoringReader.Visit("{}"u8, "monitor", true, _ => { }, new CancellationToken(true)));

    [Fact]
    public void MonitoringCollector_BoundsAndDeduplicatesScannedRows()
    {
        var now = DateTime.UtcNow;
        var collector = new TickerMonitoringCollector(now.AddHours(-1), now);
        var rows = Enumerable.Range(1, 2000).Select(i => new TickerMonitoringExecution
        {
            Id = Guid.NewGuid(), Status = TickerStatus.Done, CompletedUtc = now.AddSeconds(-i)
        }).ToArray();
        foreach (var row in rows.Concat(rows).Concat(rows.Reverse())) collector.Add(row);
        var page = collector.Build();
        Assert.Equal(513, page.Completed.Length);
        Assert.Equal(rows.Reverse().Take(513).Select(x => x.Id), page.Completed.Select(x => x.Id));
    }
}

public sealed partial class RedisRealScriptTests
{
    [Fact]
    public async Task IntegrationMonitoring_MetadataFiltersCronDefinitions_AndExcludesDispatchers()
    {
        var keys = _provider.RuntimeKeysForTest;
        var cronId = Guid.NewGuid();
        var otherCronId = Guid.NewGuid();
        await _db.StringSetAsync(keys.Cron(cronId), $$$"""{"Id":"{{{cronId}}}","Function":"monitor","Request":"not base64!"}""");
        await _db.StringSetAsync(keys.Cron(otherCronId), $$$"""{"Id":"{{{otherCronId}}}","Function":"other"}""");
        await _db.SetAddAsync(keys.CronIds, [cronId.ToString(), otherCronId.ToString()]);
        var expectedId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var runningId = Guid.NewGuid();
        foreach (var row in new[]
        {
            new { Id = expectedId, CronTickerId = cronId, Status = TickerStatus.Done, ChainRootId = (Guid?)null },
            new { Id = runningId, CronTickerId = cronId, Status = TickerStatus.InProgress, ChainRootId = (Guid?)null },
            new { Id = Guid.NewGuid(), CronTickerId = cronId, Status = TickerStatus.Done, ChainRootId = (Guid?)Guid.NewGuid() },
            new { Id = Guid.NewGuid(), CronTickerId = otherCronId, Status = TickerStatus.Done, ChainRootId = (Guid?)null }
        })
        {
            var json = JsonSerializer.Serialize(new { row.Id, row.CronTickerId, row.Status, row.ChainRootId, ExecutedAt = BaseNow, ElapsedTime = 10, AcquisitionToken = token });
            await _db.StringSetAsync(keys.CronOccurrence(row.Id), json);
            await _db.SetAddAsync(keys.CronOccurrenceIds, row.Id.ToString());
        }
        var snapshot = await _provider.ReadMonitoringSnapshotAsync("monitor", BaseNow.AddHours(-1), BaseNow);
        var completed = Assert.Single(snapshot.Completed);
        Assert.Equal(expectedId, completed.Id);
        Assert.True(completed.IsCron);
        Assert.Equal(token, completed.Generation);
        Assert.Equal(runningId, Assert.Single(snapshot.Running).Id);
    }

    [Fact]
    public async Task IntegrationMonitoring_MetadataScansAcrossPages_AndDeduplicatesEmbeddedSteps()
    {
        var keys = _provider.RuntimeKeysForTest;
        var childId = Guid.NewGuid();
        var child = $$$"""{"Id":"{{{childId}}}","Function":"monitor","Status":{{{(int)TickerStatus.Done}}},"ExecutedAt":"{{{BaseNow:O}}}","ElapsedTime":42,"Request":"not base64!","Children":{}}""";
        var runningId = Guid.NewGuid();
        var running = $$$"""{"Id":"{{{runningId}}}","Function":"monitor","Status":{{{(int)TickerStatus.InProgress}}},"Children":[]}""";
        var rootId = Guid.NewGuid();
        var root = $$$"""{"Id":"{{{rootId}}}","Function":"other","Children":[{{{child}}},{{{running}}}]}""";
        // Use the provider's actual key builder, including a standalone duplicate of an embedded step.
        await _db.StringSetAsync(keys.TimeTicker(rootId), root);
        await _db.StringSetAsync(keys.TimeTicker(childId), child);
        await _db.SetAddAsync(keys.TimeTickerIds, [rootId.ToString(), childId.ToString()]);
        for (var i = 0; i < 550; i++)
        {
            var id = Guid.NewGuid();
            var json = $$$"""{"Id":"{{{id}}}","Function":"monitor","Status":{{{(int)TickerStatus.Failed}}},"ExecutedAt":"{{{BaseNow.AddSeconds(-i - 1):O}}}","Request":"not base64!"}""";
            await _db.StringSetAsync(keys.TimeTicker(id), json);
            await _db.SetAddAsync(keys.TimeTickerIds, id.ToString());
        }
        var first = await _provider.ReadMonitoringSnapshotAsync("monitor", BaseNow.AddHours(-1), BaseNow);
        Assert.Equal(513, first.Completed.Length);
        Assert.Empty(first.Running);
        var second = await _provider.ReadMonitoringSnapshotAsync("monitor", first.Completed[511].CompletedUtc!.Value, BaseNow);
        Assert.Equal(39, second.Completed.Length);
        Assert.Equal(childId, second.Completed[^1].Id);
        Assert.Equal(42, second.Completed[^1].ElapsedMilliseconds);
        Assert.Equal(runningId, Assert.Single(second.Running).Id);
    }
}
