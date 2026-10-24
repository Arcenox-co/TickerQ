using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;

namespace TickerQ.Tests;

/// <summary>
/// AddTickerQDiscovery used to call Assembly.Load only, which does not run a module initializer,
/// so source-generated registrations in untouched assemblies were never executed.
/// </summary>
public class TickerQDiscoveryTests
{
    private const string FixtureAssembly = "TickerQ.Tests.DiscoveryFixture";
    private const string MarkerKey = "TickerQ.Tests.DiscoveryFixture.Initialized";

    // One test on purpose: a module initializer runs once per process, so the "not yet run"
    // precondition can only be observed before the first discovery call.
    [Fact]
    public void AddTickerQDiscovery_runs_the_module_initializer_of_an_untouched_assembly()
    {
        // Load by name only: no type from the fixture is touched, so its initializer has not run.
        var assembly = Assembly.Load(new AssemblyName(FixtureAssembly));
        Assert.Null(AppDomain.CurrentDomain.GetData(MarkerKey));

        var services = new ServiceCollection();
        services.AddTickerQ(options => options
            .UseApplicationNamespace("discovery-tests")
            .AddTickerQDiscovery(null!)
            .AddTickerQDiscovery([null!, assembly, assembly]));

        Assert.Equal(true, AppDomain.CurrentDomain.GetData(MarkerKey));
    }
}
