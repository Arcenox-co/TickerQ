using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

// Own entity types give this class its own static in-memory store, so its full reset cannot wipe the
// default-type partitions that tests running in parallel are activating.
public sealed class SchedulePolicyInMemoryStoreTests : IDisposable
{
    public sealed class PolicyTimeTicker : TimeTickerEntity<PolicyTimeTicker> { }

    public sealed class PolicyCronTicker : CronTickerEntity { }

    public SchedulePolicyInMemoryStoreTests()
        => TickerInMemoryPersistenceProvider<PolicyTimeTicker, PolicyCronTicker>.ResetAllStateForTests();

    [Fact]
    public async Task Crud_Is_Optimistic_And_Isolated_By_Application_Namespace()
    {
        var first = CreateStore("policy-a");
        var second = CreateStore("policy-b");
        var a = await first.CreateSchedulePolicyAsync(SchedulePolicy.Create("Business hours").Build());
        var b = await second.CreateSchedulePolicyAsync(SchedulePolicy.Create("Business hours").Build());

        Assert.NotEqual(a.ApplicationNamespaceKey, b.ApplicationNamespaceKey);
        Assert.Equal(a.Id, Assert.Single(await first.ListSchedulePoliciesAsync()).Id);
        Assert.Equal(b.Id, Assert.Single(await second.ListSchedulePoliciesAsync()).Id);

        var replacement = a.Copy();
        replacement.Description = "Updated";
        var updated = await first.UpdateSchedulePolicyAsync(replacement, a.Version);
        Assert.Equal(2, updated.Version);
        Assert.Equal("Updated", updated.Description);
        Assert.Null(await first.UpdateSchedulePolicyAsync(replacement, a.Version));
        Assert.False(await first.DeleteSchedulePolicyAsync(a.Id, a.Version));
        Assert.True(await first.DeleteSchedulePolicyAsync(a.Id, updated.Version));
        Assert.Empty(await first.ListSchedulePoliciesAsync());
        Assert.Single(await second.ListSchedulePoliciesAsync());
    }

    public void Dispose()
        => TickerInMemoryPersistenceProvider<PolicyTimeTicker, PolicyCronTicker>.ResetAllStateForTests();

    private static ISchedulePolicyStore CreateStore(string applicationNamespace)
    {
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(applicationNamespace, 1, schedulerEnabled: false);
        var services = new ServiceCollection().AddSingleton(options).BuildServiceProvider();
        return new TickerInMemoryPersistenceProvider<PolicyTimeTicker, PolicyCronTicker>(services);
    }
}
