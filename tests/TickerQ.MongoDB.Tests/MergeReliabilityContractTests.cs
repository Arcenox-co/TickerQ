using System.Reflection;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;

namespace TickerQ.MongoDB.Tests;

public class MergeReliabilityContractTests
{
    [Fact]
    public void PublicMappings_PreserveConfiguredTimeouts()
    {
        var cron = new CronTickerEntity { TimeoutSeconds = 17 };
        var projectedCron = MappingExtensions.ForCronTickerExpressions<CronTickerEntity>().Compile()(cron);

        var grandchild = new TimeTickerEntity { TimeoutSeconds = 3 };
        var child = new TimeTickerEntity { TimeoutSeconds = 5, Children = [grandchild] };
        var root = new TimeTickerEntity { TimeoutSeconds = 11, Children = [child] };
        var projectedTime = MappingExtensions.ForQueueTimeTickers<TimeTickerEntity>().Compile()(root);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity> { CronTicker = cron };
        var projectedOccurrence = MappingExtensions
            .ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<CronTickerEntity>, CronTickerEntity>()
            .Compile()(occurrence);
        var projectedLatestOccurrence = MappingExtensions
            .ForLatestQueuedCronTickerOccurrence<CronTickerOccurrenceEntity<CronTickerEntity>, CronTickerEntity>()
            .Compile()(occurrence);

        Assert.Equal(17, projectedCron.TimeoutSeconds);
        Assert.Equal(11, projectedTime.TimeoutSeconds);
        Assert.Equal(5, Assert.Single(projectedTime.Children!).TimeoutSeconds);
        Assert.Equal(3, Assert.Single(Assert.Single(projectedTime.Children!).Children!).TimeoutSeconds);
        Assert.Equal(17, projectedOccurrence.CronTicker.TimeoutSeconds);
        Assert.Equal(17, projectedLatestOccurrence.CronTicker.TimeoutSeconds);
    }

    [Theory]
    [InlineData("RenewTimeTickerLeases")]
    [InlineData("RenewCronTickerOccurrenceLeases")]
    [InlineData("GetStillHeldTickerIds")]
    [InlineData("RecoverStaleTickers")]
    public void MongoProvider_OverridesReliabilityContract(string methodName)
    {
        var providerType = typeof(TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>);
        var method = providerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(providerType, method!.DeclaringType);
    }
}
