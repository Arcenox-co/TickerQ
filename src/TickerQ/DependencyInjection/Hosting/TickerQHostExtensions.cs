using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Enums;

namespace TickerQ.DependencyInjection.Hosting
{
    public static class TickerQHostExtensions
    {
        public static void UseTickerQ(this IHost host, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
            => TickerQServiceExtensions.UseTickerQ(host.Services, qStartMode);
    }
}