using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Dashboard.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints<TTimeTicker, TCronTicker>(this IEndpointRouteBuilder endpoints, DashboardOptionsBuilder config)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        // New authentication endpoints
        endpoints.MapGet("/api/auth/info", GetAuthInfo)
            .WithName("GetAuthInfo")
            .WithSummary("Get authentication configuration")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous();
            
        endpoints.MapPost("/api/auth/validate", ValidateAuth)
            .WithName("ValidateAuth")
            .WithSummary("Validate authentication credentials")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous();
            
        var apiGroup = endpoints.MapGroup("/api").WithTags("TickerQ Dashboard").RequireCors("TickerQ_Dashboard_CORS");

        // Apply authentication if configured
        if (config.Auth.Mode == AuthMode.Host)
        {
            // For host authentication, use default authorization
            apiGroup.RequireAuthorization();
        }
        // For other auth modes (Basic, Bearer, Custom), authentication is handled by AuthMiddleware
        // API endpoints are automatically protected when auth is enabled
            
        // Options endpoint
        apiGroup.MapGet("/options", GetOptions<TTimeTicker, TCronTicker>)
            .WithName("GetOptions")
            .WithSummary("Get dashboard options and status");

        // Time Tickers endpoints
        apiGroup.MapGet("/time-tickers", GetTimeTickers<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickers")
            .WithSummary("Get all time tickers");
        
        apiGroup.MapGet("/time-tickers/paginated", GetTimeTickersPaginated<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickersPaginated")
            .WithSummary("Get paginated time tickers");

        apiGroup.MapGet("/time-tickers/graph-data-range", GetTimeTickersGraphDataRange<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickersGraphDataRange")
            .WithSummary("Get time tickers graph data for specific date range");

        apiGroup.MapGet("/time-tickers/graph-data", GetTimeTickersGraphData<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickersGraphData")
            .WithSummary("Get time tickers graph data");

        apiGroup.MapPost("/time-ticker/add", CreateChainJobs<TTimeTicker, TCronTicker>)
            .WithName("CreateChainJobs")
            .WithSummary("Create chain jobs");

        apiGroup.MapPut("/time-ticker/update", UpdateTimeTicker<TTimeTicker, TCronTicker>)
            .WithName("UpdateTimeTicker")
            .WithSummary("Update time ticker");

        apiGroup.MapDelete("/time-ticker/delete", DeleteTimeTicker<TTimeTicker, TCronTicker>)
            .WithName("DeleteTimeTicker")
            .WithSummary("Delete time ticker");

        // Cron Tickers endpoints
        apiGroup.MapGet("/cron-tickers", GetCronTickers<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickers")
            .WithSummary("Get all cron tickers");
        
        apiGroup.MapGet("/cron-tickers/paginated", GetCronTickersPaginated<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersPaginated")
            .WithSummary("Get paginated cron tickers");

        apiGroup.MapGet("/cron-tickers/graph-data-range", GetCronTickersGraphDataRange<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersGraphDataRange")
            .WithSummary("Get cron tickers graph data for specific date range");

        apiGroup.MapGet("/cron-tickers/graph-data-range-id", GetCronTickersByIdGraphDataRange<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersByIdGraphDataRange")
            .WithSummary("Get cron ticker graph data by ID for specific date range");

        apiGroup.MapGet("/cron-tickers/graph-data", GetCronTickersGraphData<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersGraphData")
            .WithSummary("Get cron tickers graph data");

        apiGroup.MapGet("/cron-ticker-occurrences/{cronTickerId}", GetCronTickerOccurrences<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickerOccurrences")
            .WithSummary("Get cron ticker occurrences");
        
        apiGroup.MapGet("/cron-ticker-occurrences/{cronTickerId}/paginated", GetCronTickerOccurrencesPaginated<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickerOccurrencesPaginated")
            .WithSummary("Get paginated cron ticker occurrences");

        apiGroup.MapGet("/cron-ticker-occurrences/{cronTickerId}/graph-data", GetCronTickerOccurrencesGraphData<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickerOccurrencesGraphData")
            .WithSummary("Get cron ticker occurrences graph data");

        apiGroup.MapPost("/cron-ticker/add", AddCronTicker<TTimeTicker, TCronTicker>)
            .WithName("AddCronTicker")
            .WithSummary("Add cron ticker");

        apiGroup.MapPut("/cron-ticker/update", UpdateCronTicker<TTimeTicker, TCronTicker>)
            .WithName("UpdateCronTicker")
            .WithSummary("Update cron ticker");

        apiGroup.MapPost("/cron-ticker/run", RunCronTickerOnDemand<TTimeTicker, TCronTicker>)
            .WithName("RunCronTickerOnDemand")
            .WithSummary("Run cron ticker on demand");

        apiGroup.MapDelete("/cron-ticker/delete", DeleteCronTicker<TTimeTicker, TCronTicker>)
            .WithName("DeleteCronTicker")
            .WithSummary("Delete cron ticker");

        apiGroup.MapDelete("/cron-ticker-occurrence/delete", DeleteCronTickerOccurrence<TTimeTicker, TCronTicker>)
            .WithName("DeleteCronTickerOccurrence")
            .WithSummary("Delete cron ticker occurrence");

        // Ticker operations
        apiGroup.MapPost("/ticker/cancel", CancelTicker<TTimeTicker, TCronTicker>)
            .WithName("CancelTicker")
            .WithSummary("Cancel ticker by ID");

        apiGroup.MapGet("/ticker-request/{id}", GetTickerRequest<TTimeTicker, TCronTicker>)
            .WithName("GetTickerRequest")
            .WithSummary("Get ticker request by ID");

        apiGroup.MapGet("/ticker-functions", GetTickerFunctions<TTimeTicker, TCronTicker>)
            .WithName("GetTickerFunctions")
            .WithSummary("Get available ticker functions");

        // Host operations
        apiGroup.MapGet("/ticker-host/next-ticker", GetNextTicker<TTimeTicker, TCronTicker>)
            .WithName("GetNextTicker")
            .WithSummary("Get next planned ticker");

        apiGroup.MapPost("/ticker-host/stop", StopTickerHost<TTimeTicker, TCronTicker>)
            .WithName("StopTickerHost")
            .WithSummary("Stop ticker host");

        apiGroup.MapPost("/ticker-host/start", StartTickerHost<TTimeTicker, TCronTicker>)
            .WithName("StartTickerHost")
            .WithSummary("Start ticker host");

        apiGroup.MapPost("/ticker-host/restart", RestartTickerHost<TTimeTicker, TCronTicker>)
            .WithName("RestartTickerHost")
            .WithSummary("Restart ticker host");

        apiGroup.MapGet("/ticker-host/status", GetTickerHostStatus<TTimeTicker, TCronTicker>)
            .WithName("GetTickerHostStatus")
            .WithSummary("Get ticker host status");

        // Statistics endpoints
        apiGroup.MapGet("/ticker/statuses/get-last-week", GetLastWeekJobStatus<TTimeTicker, TCronTicker>)
            .WithName("GetLastWeekJobStatus")
            .WithSummary("Get last week job statuses");

        apiGroup.MapGet("/ticker/statuses/get", GetJobStatuses<TTimeTicker, TCronTicker>)
            .WithName("GetJobStatuses")
            .WithSummary("Get overall job statuses");

        apiGroup.MapGet("/ticker/machine/jobs", GetMachineJobs<TTimeTicker, TCronTicker>)
            .WithName("GetMachineJobs")
            .WithSummary("Get machine jobs");

        // SignalR Hub - authentication handled in hub OnConnectedAsync
        endpoints.MapHub<TickerQNotificationHub>($"/ticker-notification-hub")
            .AllowAnonymous();

    }

    // Authorization policy helper for host authentication
    private static Action<AuthorizationPolicyBuilder> GetHostAuthorizationPolicy()
    {
        return policy => policy.RequireAuthenticatedUser();
    }

    #region Endpoint Handlers
    
    private static IResult GetAuthInfo(IAuthService authService)
    {
        var authInfo = authService.GetAuthInfo();
        
        // Return in format expected by frontend
        var response = new
        {
            mode = authInfo.Mode.ToString().ToLower(),
            enabled = authInfo.IsEnabled,
            sessionTimeout = authInfo.SessionTimeoutMinutes
        };
        
        return Results.Ok(response);
    }

    private static async Task<IResult> ValidateAuth(HttpContext context, IAuthService authService)
    {
        var authResult = await authService.AuthenticateAsync(context);
        
        if (authResult.IsAuthenticated)
        {
            return Results.Ok(new
            {
                authenticated = true,
                username = authResult.Username,
                message = "Authentication successful"
            });
        }

        return Results.Unauthorized();
    }
    

    private static IResult GetOptions<TTimeTicker, TCronTicker>(
        TickerExecutionContext executionContext, SchedulerOptionsBuilder schedulerOptions)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        return Results.Ok(new
        {
            maxConcurrency = schedulerOptions.MaxConcurrency,
            schedulerOptions.IdleWorkerTimeOut,
            currentMachine = schedulerOptions.NodeIdentifier,
            executionContext.LastHostExceptionMessage
        });
    }

    private static async Task<IResult> GetTimeTickers<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetTimeTickersAsync(cancellationToken);
        return Results.Ok(result);
    }
    
    private static async Task<IResult> GetTimeTickersPaginated<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetTimeTickersPaginatedAsync(pageNumber, pageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTimeTickersGraphDataRange<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetTimeTickersGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTimeTickersGraphData<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetTimeTickerFullDataAsync(cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateChainJobs<TTimeTicker, TCronTicker>(
        HttpContext context,
        ITimeTickerManager<TTimeTicker> timeTickerManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);
        
        // Use Dashboard-specific JSON options
        var chainRoot = JsonSerializer.Deserialize<TTimeTicker>(jsonString, dashboardOptions.DashboardJsonOptions);
        
        var result = await timeTickerManager.AddAsync(chainRoot, cancellationToken);
        
        return Results.Ok(new { 
            success = result.IsSucceeded, 
            message = result.IsSucceeded ? "Chain jobs created successfully" : "Failed to create chain jobs",
            tickerId = result.Result?.Id
        });
    }

    private static async Task<IResult> UpdateTimeTicker<TTimeTicker, TCronTicker>(
        Guid id,
        TTimeTicker request,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await repository.UpdateTimeTickerAsync(id, request, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> DeleteTimeTicker<TTimeTicker, TCronTicker>(
        Guid id,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await repository.DeleteTimeTickerByIdAsync(id, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> GetCronTickers<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickersAsync(cancellationToken);
        return Results.Ok(result);
    }
    
    private static async Task<IResult> GetCronTickersPaginated<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickersPaginatedAsync(pageNumber, pageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetCronTickersGraphDataRange<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickersGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetCronTickersByIdGraphDataRange<TTimeTicker, TCronTicker>(
        Guid id,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickersGraphSpecificDataByIdAsync(id, pastDays, futureDays, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetCronTickersGraphData<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickerFullDataAsync(cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetCronTickerOccurrences<TTimeTicker, TCronTicker>(
        Guid cronTickerId,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickersOccurrencesAsync(cronTickerId, cancellationToken);
        return Results.Ok(result);
    }
    
    private static async Task<IResult> GetCronTickerOccurrencesPaginated<TTimeTicker, TCronTicker>(
        Guid cronTickerId,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickersOccurrencesPaginatedAsync(cronTickerId, pageNumber, pageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetCronTickerOccurrencesGraphData<TTimeTicker, TCronTicker>(
        Guid cronTickerId,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await repository.GetCronTickersOccurrencesGraphDataAsync(cronTickerId, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> AddCronTicker<TTimeTicker, TCronTicker>(
        TCronTicker request,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await repository.AddCronTickerAsync(request, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> UpdateCronTicker<TTimeTicker, TCronTicker>(
        Guid id,
        UpdateCronTickerRequest request,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await repository.UpdateCronTickerAsync(id, request, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> RunCronTickerOnDemand<TTimeTicker, TCronTicker>(
        Guid id,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await repository.AddOnDemandCronTickerOccurrenceAsync(id, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> DeleteCronTicker<TTimeTicker, TCronTicker>(
        Guid id,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await repository.DeleteCronTickerByIdAsync(id, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> DeleteCronTickerOccurrence<TTimeTicker, TCronTicker>(
        Guid id,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await repository.DeleteCronTickerOccurrenceByIdAsync(id, cancellationToken);
        return Results.Ok();
    }

    private static IResult CancelTicker<TTimeTicker, TCronTicker>(
        Guid id,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (repository.CancelTickerById(id))
            return Results.Ok();

        return Results.BadRequest();
    }

    private static async Task<IResult> GetTickerRequest<TTimeTicker, TCronTicker>(
        Guid tickerId,
        TickerType tickerType,
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var resultData = await repository.GetTickerRequestByIdAsync(tickerId, tickerType, cancellationToken);

        var response = new
        {
            Result = resultData.Item1,
            MatchType = resultData.Item2,
        };
        return Results.Ok(response);
    }

    private static IResult GetTickerFunctions<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = repository.GetTickerFunctions().Select(x => new
        {
            FunctionName = x.Item1,
            FunctionRequestNamespace = x.Item2.Item1,
            FunctionRequestType = x.Item2.Item2,
            Priority = (int)x.Item2.Item3,
        });

        return Results.Ok(result);
    }

    private static IResult GetNextTicker<TTimeTicker, TCronTicker>(
        TickerExecutionContext executionContext)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = new
        {
            NextOccurrence = executionContext.GetNextPlannedOccurrence()
        };
        return Results.Ok(result);
    }

    private static async Task<IResult> StopTickerHost<TTimeTicker, TCronTicker>(ITickerQHostScheduler scheduler)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await scheduler.StopAsync();
        return Results.Ok();
    }

    private static async Task<IResult> StartTickerHost<TTimeTicker, TCronTicker>(ITickerQHostScheduler scheduler)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await scheduler.StartAsync();
        return Results.Ok();
    }

    private static IResult RestartTickerHost<TTimeTicker, TCronTicker>(ITickerQHostScheduler scheduler)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        scheduler.Restart();
        return Results.Ok();
    }

    private static IResult GetTickerHostStatus<TTimeTicker, TCronTicker>(ITickerQHostScheduler scheduler)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        return Results.Ok(new { scheduler.IsRunning });
    }

    private static async Task<IResult> GetLastWeekJobStatus<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var jobStatuses = await repository.GetLastWeekJobStatusesAsync(cancellationToken);
        return Results.Ok(jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray());
    }

    private static async Task<IResult> GetJobStatuses<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var jobStatuses = await repository.GetOverallJobStatusesAsync(cancellationToken);
        return Results.Ok(jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray());
    }

    private static async Task<IResult> GetMachineJobs<TTimeTicker, TCronTicker>(
        ITickerDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var machineJobs = await repository.GetMachineJobsAsync(cancellationToken);
        return Results.Ok(machineJobs.Select(x => new { item1 = x.Item1, item2 = x.Item2 }).ToArray());
    }

    #endregion
}
