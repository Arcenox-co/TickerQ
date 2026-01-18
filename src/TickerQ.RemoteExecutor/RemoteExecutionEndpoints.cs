using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.RemoteExecutor.Models;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Extension methods for mapping HTTP endpoints that the TickerQ SDK uses.
/// These endpoints translate incoming HTTP calls into calls on TickerQ's
/// persistence layer and internal managers.
/// </summary>
public static class RemoteExecutionEndpoints
{
    /// <summary>
    /// Maps all TickerQ remote execution endpoints using the default entity types.
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQRemoteExecutionEndpoints(
        this IEndpointRouteBuilder endpoints, string prefix = "")
    {
        return endpoints.MapTickerQRemoteExecutionEndpoints<TimeTickerEntity, CronTickerEntity>(prefix);
    }

    /// <summary>
    /// Maps all TickerQ remote execution endpoints for the specified ticker types.
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQRemoteExecutionEndpoints<TTimeTicker, TCronTicker>(
        this IEndpointRouteBuilder endpoints, string prefix = "")
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

        // Base group; the host can apply any path prefix by mapping this group under a route.
        var group = endpoints.MapGroup(prefix);

        MapFunctionRegistration(group);
        MapTimeTickerEndpoints<TTimeTicker, TCronTicker>(group);
        MapCronTickerEndpoints<TTimeTicker, TCronTicker>(group);
        MapCronOccurrenceEndpoints<TTimeTicker, TCronTicker>(group);

        return endpoints;
    }

    private static void MapFunctionRegistration(IEndpointRouteBuilder group)
    {
        group.MapPost("functions/register",
            async (RemoteTickerFunctionDescriptor[] newFunctions,
                IInternalTickerManager internalTickerManager,
                CancellationToken cancellationToken) =>
            {
                // At this stage we only care about cron expressions for seeding.
                if (newFunctions.Length == 0)
                    return Results.Ok();

                var cronPairs = newFunctions
                    .Where(f => !string.IsNullOrWhiteSpace(f.CronExpression))
                    .Select(f => (f.Name, f.CronExpression))
                    .ToArray();

                var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();

                foreach (var newFunction in newFunctions)
                {
                    var newFunctionDelegate = new TickerFunctionDelegate(async (ct, serviceProvider, context) =>
                    {
                        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                        var httpClient = httpClientFactory.CreateClient("tickerq-callback");
                        
                        // Build a minimal payload describing the execution
                        var payload = new
                        {
                            context.Id,
                            context.FunctionName,
                            context.Type,
                            context.RetryCount,
                            context.ScheduledFor
                        };

                        // newFunction.Callback should be the full URL of the remote endpoint
                        using var response = await httpClient.PostAsJsonAsync(
                            new Uri($"{newFunction.Callback}execute"),
                            payload,
                            ct);

                        response.EnsureSuccessStatusCode();
                    });
                    
                    functionDict.TryAdd(newFunction.Name, (newFunction.CronExpression, newFunction.Priority, newFunctionDelegate));
                }
                
                TickerFunctionProvider.RegisterFunctions(functionDict);
                TickerFunctionProvider.Build();
                
                if (cronPairs.Length > 0)
                    await internalTickerManager.MigrateDefinedCronTickers(cronPairs, cancellationToken)
                        .ConfigureAwait(false);

                return Results.Ok();
            });
    }

    private static void MapTimeTickerEndpoints<TTimeTicker, TCronTicker>(IEndpointRouteBuilder group)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        group.MapPost("time-tickers",
            async (TTimeTicker[] tickers,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var affected = await provider.AddTimeTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPut("time-tickers",
            async (TTimeTicker[] tickers,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var affected = await provider.UpdateTimeTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPost("time-tickers/delete",
            async (Guid[] ids,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var affected = await provider.RemoveTimeTickers(ids, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPut("time-tickers/context",
            async (InternalFunctionContext context,
                IInternalTickerManager internalTickerManager,
                CancellationToken cancellationToken) =>
            {
                // Let InternalTickerManager route to the correct persistence methods and handle notifications.
                await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
                return Results.Ok(1);
            });

        group.MapPost("time-tickers/unified-context",
            async (TimeTickerUnifiedContextRequest payload,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                if (payload?.Ids == null || payload.Context == null)
                    return Results.BadRequest("Ids and context are required.");

                await provider.UpdateTimeTickersWithUnifiedContext(payload.Ids, payload.Context, cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok();
            });
    }

    private static void MapCronTickerEndpoints<TTimeTicker, TCronTicker>(IEndpointRouteBuilder group)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        group.MapPost("cron-tickers",
            async (TCronTicker[] tickers,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var affected = await provider.InsertCronTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPut("cron-tickers",
            async (TCronTicker[] tickers,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var affected = await provider.UpdateCronTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPost("cron-tickers/delete",
            async (Guid[] ids,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var affected = await provider.RemoveCronTickers(ids, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });
    }

    private static void MapCronOccurrenceEndpoints<TTimeTicker, TCronTicker>(IEndpointRouteBuilder group)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        group.MapPut("cron-ticker-occurrences/context",
            async (InternalFunctionContext context,
                IInternalTickerManager internalTickerManager,
                CancellationToken cancellationToken) =>
            {
                // Same as time tickers: delegate to InternalTickerManager.
                await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
                return Results.Ok();
            });
    }

    private sealed class TimeTickerUnifiedContextRequest
    {
        public Guid[] Ids { get; set; } = [];
        public InternalFunctionContext Context { get; set; }
    }
}
