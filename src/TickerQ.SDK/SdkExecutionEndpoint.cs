using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.SDK.Infrastructure;
using TickerQ.SDK.Models;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.SDK;

public static class SdkExecutionEndpoint
{
    
    public static IEndpointRouteBuilder ExposeSdkExecutionEndpoint(
        this IEndpointRouteBuilder endpoints, string prefix = "")
    {
        var group = endpoints.MapGroup(prefix);
        group.MapPost("/execute", async (HttpContext http, [FromServices] IServiceProvider serviceProvider) =>
        {
            http.Request.EnableBuffering();

            http.Request.Body.Position = 0;

            using var reader = new StreamReader(
                http.Request.Body,
                Encoding.UTF8,
                leaveOpen: true);

            var body = await reader.ReadToEndAsync();

            http.Request.Body.Position = 0; // important if anything else reads it

            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest("Empty body");

            RemoteExecutionContext? context;
            try
            {
                context = JsonSerializer.Deserialize<RemoteExecutionContext>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return Results.BadRequest("Invalid JSON payload");
            }

            if (context is null || string.IsNullOrWhiteSpace(context.FunctionName))
                return Results.BadRequest("Invalid payload");
            
            await using var scope = serviceProvider.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetService<ITickerQTaskScheduler>();
            var taskHandler = scope.ServiceProvider.GetRequiredService<ITickerExecutionTaskHandler>();
            
            var function = new InternalFunctionContext
            {
                FunctionName = context.FunctionName,
                TickerId = context.Id,
                ParentId = null,
                Type = context.Type,
                Retries = 0,
                RetryCount = context.RetryCount,
                Status = TickerStatus.Idle,
                ExecutionTime = context.ScheduledFor,
                RunCondition = RunCondition.OnSuccess
            };
            
            if (TickerFunctionProvider.TickerFunctions.TryGetValue(function.FunctionName, out var tickerItem))
            {
                function.CachedDelegate = tickerItem.Delegate;
                function.CachedPriority = tickerItem.Priority;
            }
            
            if (scheduler is not null && !scheduler.IsDisposed && !scheduler.IsFrozen)
            {
                await scheduler.QueueAsync(
                    ct => taskHandler.ExecuteTaskAsync(function, context.IsDue, ct),
                    TickerTaskPriority.LongRunning,
                    cancellationToken: CancellationToken.None);
            }
            else
            {
                await taskHandler.ExecuteTaskAsync(function, context.IsDue, CancellationToken.None);
            }
            
            return Results.Ok();
        }).AddEndpointFilter<TickerQSignatureFilter>();
        group.MapPost("/resync", async ([FromServices] TickerQFunctionSyncService syncService) =>
        {
            await syncService.SyncAsync(CancellationToken.None).ConfigureAwait(false);
            return Results.Ok();
        }).AddEndpointFilter<TickerQSignatureFilter>();

         
        return endpoints;
    } 
}
