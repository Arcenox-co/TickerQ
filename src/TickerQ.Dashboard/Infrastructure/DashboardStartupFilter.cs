using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard.Infrastructure;

internal class DashboardStartupFilter<TTimeTicker, TCronTicker> : IStartupFilter
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly DashboardOptionsBuilder _config;

    public DashboardStartupFilter(DashboardOptionsBuilder config)
    {
        _config = config;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Only apply if not already applied by UseDashboardDelegate (new WebApplication pattern)
            if (!_config.MiddlewareApplied)
            {
                _config.MiddlewareApplied = true;
                app.UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(_config);
            }

            next(app);
        };
    }
}
