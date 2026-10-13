using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.MongoDB;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Tests;

public sealed class MongoBootstrapRegistrationTests
{
    private sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker> { }
    private sealed class TestCronTicker : CronTickerEntity { }

    [Fact]
    public void MongoProvisioner_UsesExplicitBootstrapAndFinalizer_NotIndependentHostedService()
    {
        var builder = new TickerQMongoOptionBuilder<TestTimeTicker, TestCronTicker>();
        builder.UseTickerQMongoClient("mongodb://localhost:27017", "tickerq-registration-test");
        var services = new ServiceCollection();

        builder.ConfigureServices(services);

        Assert.Single(services, x => x.ServiceType == typeof(ITickerQPersistenceBootstrapper));
        Assert.Single(services, x => x.ServiceType == typeof(ITickerQPersistenceFinalizer));
        Assert.Single(services, x => x.ServiceType == typeof(ITickerQPersistenceReadinessProbe));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService));
    }
}
