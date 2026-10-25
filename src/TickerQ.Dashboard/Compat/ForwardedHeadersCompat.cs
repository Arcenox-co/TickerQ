using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Builder;

namespace TickerQ.Dashboard.Compat;

/// <summary>
/// Trusts exactly the configured proxies and networks. .NET 10 adds <c>KnownIPNetworks</c> (System.Net.IPNetwork)
/// and obsoletes <c>KnownNetworks</c>; earlier runtimes only have the latter.
/// </summary>
internal static class ForwardedHeadersCompat
{
    public static void TrustOnly(ForwardedHeadersOptions options, IEnumerable<IPAddress> proxies,
        IEnumerable<System.Net.IPNetwork> networks)
    {
        options.KnownProxies.Clear();
        foreach (var proxy in proxies)
            options.KnownProxies.Add(proxy);
#if NET10_0_OR_GREATER
        options.KnownIPNetworks.Clear();
        foreach (var network in networks)
            options.KnownIPNetworks.Add(network);
#else
        options.KnownNetworks.Clear();
        foreach (var network in networks)
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(network.BaseAddress, network.PrefixLength));
#endif
    }
}
