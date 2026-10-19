using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Infrastructure;
using Xunit;

namespace TickerQ.Tests;

public class RequestContractIdentityMappingTests
{
    [Fact]
    public void QueueTimeProjection_PreservesIdentityAcrossRootChildAndGrandchild()
    {
        var grandchild = Time("Grandchild", 3, "grandchild");
        var child = Time("Child", 2, "child");
        child.Children.Add(grandchild);
        var root = Time("Root", 1, "root");
        root.Children.Add(child);

        var projected = MappingExtensions.ForQueueTimeTickers<TimeTickerEntity>().Compile()(root);

        AssertIdentity(projected, 1, "root");
        AssertIdentity(Assert.Single(projected.Children), 2, "child");
        AssertIdentity(Assert.Single(Assert.Single(projected.Children).Children), 3, "grandchild");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void QueueTimeProjection_ExcludesScheduledBranches(int scheduledDepth)
    {
        var root = Time("Root", 1, "root");
        var inlineChild = Time("InlineChild", 2, "inline-child");
        var inlineGrandchild = Time("InlineGrandchild", 3, "inline-grandchild");
        root.Children.Add(inlineChild);
        inlineChild.Children.Add(inlineGrandchild);
        var scheduled = Time("Scheduled", 4, "scheduled");
        scheduled.ExecutionTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        scheduled.Children.Add(Time("ScheduledChild", 5, "scheduled-child"));
        (scheduledDepth == 1 ? root : inlineChild).Children.Add(scheduled);

        var project = MappingExtensions.ForQueueTimeTickers<TimeTickerEntity>().Compile();
        var projected = project(root);
        var child = Assert.Single(projected.Children);
        Assert.Equal(inlineChild.Id, child.Id);
        Assert.Equal(inlineGrandchild.Id, Assert.Single(child.Children).Id);
        var independentlyProjected = project(scheduled);
        Assert.Equal(scheduled.ExecutionTime, independentlyProjected.ExecutionTime);
        Assert.Equal("ScheduledChild", Assert.Single(independentlyProjected.Children).Function);
    }

    [Fact]
    public void CronProjections_PreserveIdentity()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "Cron",
            RequestContractVersion = 7,
            RequestContractFingerprint = "cron"
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            CronTicker = cron
        };

        var expression = MappingExtensions.ForCronTickerExpressions<CronTickerEntity>().Compile()(cron);
        var queued = MappingExtensions
            .ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<CronTickerEntity>, CronTickerEntity>()
            .Compile()(occurrence);
        var latest = MappingExtensions
            .ForLatestQueuedCronTickerOccurrence<CronTickerOccurrenceEntity<CronTickerEntity>, CronTickerEntity>()
            .Compile()(occurrence);

        AssertIdentity(expression, 7, "cron");
        AssertIdentity(queued.CronTicker, 7, "cron");
        AssertIdentity(latest.CronTicker, 7, "cron");
    }

    private static TimeTickerEntity Time(string function, int version, string fingerprint) => new()
    {
        Id = Guid.NewGuid(),
        Function = function,
        RequestContractVersion = version,
        RequestContractFingerprint = fingerprint
    };

    private static void AssertIdentity(BaseTickerEntity ticker, int version, string fingerprint)
    {
        Assert.Equal(version, ticker.RequestContractVersion);
        Assert.Equal(fingerprint, ticker.RequestContractFingerprint);
    }
}
