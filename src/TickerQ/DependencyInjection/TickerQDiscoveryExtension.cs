using System.Reflection;
using System.Runtime.CompilerServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.DependencyInjection;

public static class TickerQDiscoveryExtension
{
    /// <summary>
    /// Runs the source-generated function registration of each assembly. Generated registration
    /// lives in a module initializer, which .NET runs only when code first touches the assembly;
    /// loading it is not enough. Use this for assemblies whose functions the host never references
    /// directly, for example plug-in or feature libraries.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerQDiscovery<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Assembly[] assemblies)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        foreach (var assembly in assemblies ?? [])
        {
            if (assembly == null)
                continue;
            // Idempotent: the runtime runs each module initializer at most once.
            foreach (var module in assembly.GetModules())
                RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);
        }

        return tickerConfiguration;
    }
}
