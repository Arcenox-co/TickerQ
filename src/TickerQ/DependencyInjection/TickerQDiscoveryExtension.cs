using System.Reflection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.DependencyInjection;

public static class TickerQDiscoveryExtension
{
    private const string GeneratedClassSuffix = "TickerQInstanceFactoryExtensions";
    
    /// <summary>
    /// Loads the assemblies to initialize the source generated code.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerQDiscovery<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, 
        Assembly[] assemblies)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var assembliesToLoad = assemblies ?? [];

        foreach (var assembly in assembliesToLoad)
        {
            if(!string.IsNullOrEmpty(assembly.FullName))
                Assembly.Load(assembly.FullName);
        }
        
        return tickerConfiguration;
    }
}