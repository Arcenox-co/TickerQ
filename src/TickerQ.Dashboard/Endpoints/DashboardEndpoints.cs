using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Dashboard.Endpoints;

#pragma warning disable IL2026
#pragma warning disable IL3050
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints<TTimeTicker, TCronTicker>(this IEndpointRouteBuilder endpoints, DashboardOptionsBuilder config)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        // New authentication endpoints
        WithGroupNameIfSet(endpoints.MapGet("/api/auth/info", GetAuthInfo)
            .WithName("GetAuthInfo")
            .WithSummary("Get authentication configuration")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous(), config);

        WithGroupNameIfSet(endpoints.MapPost("/api/auth/validate", ValidateAuth)
            .WithName("ValidateAuth")
            .WithSummary("Validate authentication credentials")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous(), config);

        // Resolve DashboardOptionsBuilder from HttpContext (like every other handler) rather than as an
        // implicit handler parameter. On .NET 10, RequestDelegateFactory infers the binding source of a
        // GET lambda parameter: if DashboardOptionsBuilder isn't resolvable as a service at build time it
        // is inferred as a body parameter (illegal on GET), and if the parameter name is stripped (e.g. by
        // an assembly obfuscator applied downstream) the name-keyed TrackedParameters dictionary throws
        // "An item with the same key has already been added. Key: " at endpoint build. A single
        // HttpContext parameter sidesteps inference entirely.
        WithGroupNameIfSet(endpoints.MapGet("/auth/challenge", (HttpContext httpContext) =>
        {
            var dashboardOptions = httpContext.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
            return dashboardOptions.Auth.Mode == AuthMode.Host ? Results.Challenge() : Results.Unauthorized();
        })
            .ExcludeFromDescription()
            .AllowAnonymous(), config);
            
        var apiGroup = endpoints.MapGroup("/api").WithTags("TickerQ Dashboard").RequireCors("TickerQ_Dashboard_CORS");
        WithGroupNameIfSet(apiGroup, config);

        // Apply authentication if configured
        if (config.Auth.Mode == AuthMode.Host)
        {
            // For host authentication, use configured policy or default authorization
            if (!string.IsNullOrEmpty(config.Auth.HostAuthorizationPolicy))
            {
                apiGroup.RequireAuthorization(config.Auth.HostAuthorizationPolicy);
            }
            else
            {
                apiGroup.RequireAuthorization();
            }
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

        apiGroup.MapDelete("/time-ticker/delete-batch", DeleteTimeTickersBatch<TTimeTicker, TCronTicker>)
            .WithName("DeleteTimeTickersBatch")
            .WithSummary("Delete multiple time tickers");

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

        apiGroup.MapPut("/cron-ticker/toggle", ToggleCronTicker<TTimeTicker, TCronTicker>)
            .WithName("ToggleCronTicker")
            .WithSummary("Toggle cron ticker enabled/disabled");

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

        // Optional periodic-ticker endpoints (registered only when EnablePeriodic<T>() is configured).
        if (config.PeriodicEnabled && config.PeriodicTickerType != null)
        {
            var mapMethod = typeof(PeriodicDashboardEndpoints)
                .GetMethod(nameof(PeriodicDashboardEndpoints.MapPeriodicEndpoints),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(config.PeriodicTickerType);
            mapMethod.Invoke(null, new object[] { apiGroup });
        }

        // SignalR Hub - authentication handled in hub OnConnectedAsync
        endpoints.MapHub<TickerQNotificationHub>($"/ticker-notification-hub")
            .AllowAnonymous();

        // Opt-in diagnostic (set TICKERQ_DIAG_ENDPOINTS=1). Reproduces the EXACT failing path from host
        // startup — GroupEndpointDataSource.GetGroupedEndpoints → inner RouteEndpointDataSource builds each
        // RouteEndpoint with the GROUP conventions applied (incl. RequireAuthorization) — one route at a
        // time, so the offending route's RawText is printed instead of an opaque "same key" crash.
        // A plain per-handler InferMetadata (without the grouped RouteGroupContext + conventions) does NOT
        // hit this path, which is why earlier diagnostics found nothing. No-op unless enabled.
        if (Environment.GetEnvironmentVariable("TICKERQ_DIAG_ENDPOINTS") == "1")
        {
            DiagnoseGroupedMaterialization(apiGroup, endpoints.ServiceProvider);
        }

    }

    // Faithfully replicates GroupEndpointDataSource.GetGroupedEndpoints per-entry (see RouteGroupBuilder in
    // dotnet/aspnetcore) to pinpoint which grouped route trips RequestDelegateFactory at build time.
    private static void DiagnoseGroupedMaterialization(RouteGroupBuilder apiGroup, IServiceProvider services)
    {
        try
        {
            const System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            // Group conventions (RequireAuthorization etc.) live on RouteGroupBuilder._conventions.
            var conventions = (System.Collections.Generic.List<Action<Microsoft.AspNetCore.Builder.EndpointBuilder>>)
                typeof(RouteGroupBuilder).GetField("_conventions", NP)!.GetValue(apiGroup)!;
            var finallyConventions = (System.Collections.Generic.List<Action<Microsoft.AspNetCore.Builder.EndpointBuilder>>)
                typeof(RouteGroupBuilder).GetField("_finallyConventions", NP)!.GetValue(apiGroup)!;

            // The group's inner data source is the RouteEndpointDataSource that holds the mapped routes.
            var innerDs = ((IEndpointRouteBuilder)apiGroup).DataSources.FirstOrDefault(d => d.GetType().Name == "RouteEndpointDataSource");
            if (innerDs == null)
            {
                Console.WriteLine("[TQ-DIAG] could not locate inner RouteEndpointDataSource on the /api group.");
                return;
            }

            var prefix = Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/api");
            var entries = (System.Collections.IEnumerable)innerDs.GetType().GetField("_routeEntries", NP)!.GetValue(innerDs)!;
            var createBuilder = innerDs.GetType().GetMethod("CreateRouteEndpointBuilder", NP)!;

            int ok = 0, i = 0;
            foreach (var entry in entries)
            {
                string raw = "(unknown)";
                try
                {
                    var pattern = entry.GetType().GetProperty("RoutePattern")!.GetValue(entry);
                    raw = (string)(pattern?.GetType().GetProperty("RawText")?.GetValue(pattern) ?? "(null)");

                    // CreateRouteEndpointBuilder(entry, groupPrefix, groupConventions, groupFinallyConventions)
                    var builder = createBuilder.Invoke(innerDs, new object[] { entry, prefix, conventions, finallyConventions });
                    builder!.GetType().GetMethod("Build")!.Invoke(builder, null);
                    ok++;
                }
                catch (Exception ex)
                {
                    var inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    var handlerName = "(unknown)";
                    try
                    {
                        var del = (Delegate)entry.GetType().GetProperty("RouteHandler")!.GetValue(entry)!;
                        handlerName = $"{del.Method.DeclaringType?.Name}.{del.Method.Name}";
                    }
                    catch { }
                    Console.WriteLine($"[TQ-DIAG] *** OFFENDING GROUPED ROUTE #{i} '{raw}' handler={handlerName} -> {inner.GetType().FullName}: {inner.Message}");
                }
                i++;
            }
            Console.WriteLine($"[TQ-DIAG] grouped materialization: {ok}/{i} routes built OK (with group conventions applied).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TQ-DIAG] grouped-materialization diagnostic could not run: {ex.GetType().Name}: {ex.Message}");
        }
    }
    #region Endpoint Handlers

    private static IEndpointConventionBuilder WithGroupNameIfSet(IEndpointConventionBuilder builder, DashboardOptionsBuilder config)
    {
        if (!string.IsNullOrWhiteSpace(config.GroupName))
        {
            builder.WithGroupName(config.GroupName);
        }

        return builder;
    }

    private static Task WriteJson<T>(HttpContext context, T value, JsonSerializerOptions options)
    {
        return Results.Json(value, options.GetTypeInfo(typeof(T))).ExecuteAsync(context);
    }

    private static async Task GetAuthInfo(HttpContext context)
    {
        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        var authInfo = authService.GetAuthInfo();

        var response = new AuthInfoResponse
        {
            Mode = authInfo.Mode.ToString().ToLower(),
            Enabled = authInfo.IsEnabled,
            SessionTimeout = authInfo.SessionTimeoutMinutes
        };

        await WriteJson(context, response, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task ValidateAuth(HttpContext context)
    {
        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        var authResult = await authService.AuthenticateAsync(context);

        if (authResult.IsAuthenticated)
        {
            await WriteJson(context, new AuthValidateResponse
            {
                Authenticated = true,
                Username = authResult.Username,
                Message = "Authentication successful"
            }, dashboardOptions.DashboardJsonOptions);
            return;
        }

        if (dashboardOptions.Auth.Mode == AuthMode.Host)
        {
            await context.ChallengeAsync();
            return;
        }

        context.Response.StatusCode = 401;
    }


    private static async Task GetOptions<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var executionContext = context.RequestServices.GetRequiredService<TickerExecutionContext>();
        var schedulerOptions = context.RequestServices.GetRequiredService<SchedulerOptionsBuilder>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        await WriteJson(context, new DashboardOptionsResponse
        {
            MaxConcurrency = schedulerOptions.MaxConcurrency,
            IdleWorkerTimeOut = schedulerOptions.IdleWorkerTimeOut,
            CurrentMachine = schedulerOptions.NodeIdentifier,
            LastHostExceptionMessage = executionContext.LastHostExceptionMessage,
            SchedulerTimeZone = ToIanaTimeZoneId(schedulerOptions.SchedulerTimeZone)
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetTimeTickers<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var result = await repository.GetTimeTickersAsync(cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetTimeTickersPaginated<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        int.TryParse(context.Request.Query["pageNumber"].ToString(), out var pageNumber);
        if (pageNumber < 1) pageNumber = 1;
        int.TryParse(context.Request.Query["pageSize"].ToString(), out var pageSize);
        if (pageSize < 1) pageSize = 20;

        TickerStatus? status = Enum.TryParse<TickerStatus>(context.Request.Query["status"].ToString(), ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : null;
        var search = context.Request.Query["search"].ToString();

        var result = await repository.GetTimeTickersPaginatedAsync(pageNumber, pageSize, status, search, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetTimeTickersGraphDataRange<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        if (!int.TryParse(context.Request.Query["pastDays"].ToString(), out var pastDays))
            pastDays = -3;
        if (!int.TryParse(context.Request.Query["futureDays"].ToString(), out var futureDays))
            futureDays = 3;

        var result = await repository.GetTimeTickersGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetTimeTickersGraphData<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var result = await repository.GetTimeTickerFullDataAsync(cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task CreateChainJobs<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var timeTickerManager = context.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var timeZoneId = context.Request.Query["timeZoneId"].ToString();

        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var chainRoot = (TTimeTicker)JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TTimeTicker)));

        if (chainRoot?.ExecutionTime is DateTime executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var unspecified = DateTime.SpecifyKind(executionTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            chainRoot.ExecutionTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        var result = await timeTickerManager.AddAsync(chainRoot, cancellationToken);

        await WriteJson(context, new ActionResponseWithId
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Chain jobs created successfully" : "Failed to create chain jobs",
            TickerId = result.Result?.Id
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task UpdateTimeTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var timeTickerManager = context.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var id = Guid.Parse(context.Request.Query["id"].ToString());
        var timeZoneId = context.Request.Query["timeZoneId"].ToString();

        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var timeTicker = (TTimeTicker)JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TTimeTicker)));

        // Ensure the ID matches
        timeTicker.Id = id;

        if (timeTicker.ExecutionTime is DateTime executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var unspecified = DateTime.SpecifyKind(executionTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            timeTicker.ExecutionTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        var result = await timeTickerManager.UpdateAsync(timeTicker, cancellationToken);

        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Time ticker updated successfully" : "Failed to update time ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task DeleteTimeTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var timeTickerManager = context.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        var result = await timeTickerManager.DeleteAsync(id, cancellationToken);

        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Time ticker deleted successfully" : "Failed to delete time ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task DeleteTimeTickersBatch<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var timeTickerManager = context.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        // Read body as Guid[]
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);
        var ids = (Guid[])JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(Guid[])));

        var idList = ids is { Length: > 0 } ? new List<Guid>(ids) : new List<Guid>();
        var result = await timeTickerManager.DeleteBatchAsync(idList, cancellationToken);

        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Time tickers deleted successfully" : "Failed to delete time tickers"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickers<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var result = await repository.GetCronTickersAsync(cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickersPaginated<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        int.TryParse(context.Request.Query["pageNumber"].ToString(), out var pageNumber);
        if (pageNumber < 1) pageNumber = 1;
        int.TryParse(context.Request.Query["pageSize"].ToString(), out var pageSize);
        if (pageSize < 1) pageSize = 20;

        var result = await repository.GetCronTickersPaginatedAsync(pageNumber, pageSize, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickersGraphDataRange<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        if (!int.TryParse(context.Request.Query["pastDays"].ToString(), out var pastDays))
            pastDays = -3;
        if (!int.TryParse(context.Request.Query["futureDays"].ToString(), out var futureDays))
            futureDays = 3;

        var result = await repository.GetCronTickersGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickersByIdGraphDataRange<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var id = Guid.Parse(context.Request.Query["id"].ToString());
        if (!int.TryParse(context.Request.Query["pastDays"].ToString(), out var pastDays))
            pastDays = -3;
        if (!int.TryParse(context.Request.Query["futureDays"].ToString(), out var futureDays))
            futureDays = 3;

        var result = await repository.GetCronTickersGraphSpecificDataByIdAsync(id, pastDays, futureDays, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickersGraphData<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var result = await repository.GetCronTickerFullDataAsync(cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickerOccurrences<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var cronTickerId = Guid.Parse(context.Request.RouteValues["cronTickerId"]?.ToString()!);

        var result = await repository.GetCronTickersOccurrencesAsync(cronTickerId, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickerOccurrencesPaginated<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var cronTickerId = Guid.Parse(context.Request.RouteValues["cronTickerId"]?.ToString()!);

        int.TryParse(context.Request.Query["pageNumber"].ToString(), out var pageNumber);
        if (pageNumber < 1) pageNumber = 1;
        int.TryParse(context.Request.Query["pageSize"].ToString(), out var pageSize);
        if (pageSize < 1) pageSize = 20;

        var result = await repository.GetCronTickersOccurrencesPaginatedAsync(cronTickerId, pageNumber, pageSize, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetCronTickerOccurrencesGraphData<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var cronTickerId = Guid.Parse(context.Request.RouteValues["cronTickerId"]?.ToString()!);

        var result = await repository.GetCronTickersOccurrencesGraphDataAsync(cronTickerId, cancellationToken);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task AddCronTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var cronTickerManager = context.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var cronTicker = (TCronTicker)JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TCronTicker)));

        var result = await cronTickerManager.AddAsync(cronTicker, cancellationToken);

        await WriteJson(context, new ActionResponseWithId
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Cron ticker added successfully" : "Failed to add cron ticker",
            TickerId = result.Result?.Id
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task UpdateCronTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var cronTickerManager = context.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var cronTicker = (TCronTicker)JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TCronTicker)));

        // Ensure the ID matches
        cronTicker.Id = id;

        var result = await cronTickerManager.UpdateAsync(cronTicker, cancellationToken);

        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Cron ticker updated successfully" : "Failed to update cron ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task ToggleCronTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var id = Guid.Parse(context.Request.Query["id"].ToString());
        bool.TryParse(context.Request.Query["isEnabled"].ToString(), out var isEnabled);

        var success = await repository.ToggleCronTickerAsync(id, isEnabled, cancellationToken);

        await WriteJson(context, new ActionResponse
        {
            Success = success,
            Message = success ? $"Cron ticker {(isEnabled ? "enabled" : "disabled")} successfully" : "Failed to toggle cron ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task RunCronTickerOnDemand<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var cancellationToken = context.RequestAborted;
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        await repository.AddOnDemandCronTickerOccurrenceAsync(id, cancellationToken);
        context.Response.StatusCode = 200;
    }

    private static async Task DeleteCronTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var cronTickerManager = context.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        var result = await cronTickerManager.DeleteAsync(id, cancellationToken);

        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Cron ticker deleted successfully" : "Failed to delete cron ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task DeleteCronTickerOccurrence<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var cancellationToken = context.RequestAborted;
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        await repository.DeleteCronTickerOccurrenceByIdAsync(id, cancellationToken);
        context.Response.StatusCode = 200;
    }

    private static Task CancelTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        if (repository.CancelTickerById(id))
            context.Response.StatusCode = 200;
        else
            context.Response.StatusCode = 400;

        return Task.CompletedTask;
    }

    private static async Task GetTickerRequest<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;
        var tickerId = Guid.Parse(context.Request.RouteValues["id"]?.ToString()!);
        Enum.TryParse<TickerType>(context.Request.Query["tickerType"].ToString(), out var tickerType);

        var resultData = await repository.GetTickerRequestByIdAsync(tickerId, tickerType, cancellationToken);

        var response = new TickerRequestResponse
        {
            Result = resultData.Item1,
            MatchType = resultData.Item2,
        };
        await WriteJson(context, response, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetTickerFunctions<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        var result = repository.GetTickerFunctions().Select(x => new TickerFunctionResponse
        {
            FunctionName = x.Item1,
            FunctionRequestNamespace = x.Item2.Item1,
            FunctionRequestType = x.Item2.Item2,
            Priority = (int)x.Item2.Item3,
        }).ToArray();

        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetNextTicker<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var executionContext = context.RequestServices.GetRequiredService<TickerExecutionContext>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        var result = new NextTickerResponse
        {
            NextOccurrence = executionContext.GetNextPlannedOccurrence()
        };
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task StopTickerHost<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var scheduler = context.RequestServices.GetRequiredService<ITickerQHostScheduler>();

        await scheduler.StopAsync();
        context.Response.StatusCode = 200;
    }

    private static async Task StartTickerHost<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var scheduler = context.RequestServices.GetRequiredService<ITickerQHostScheduler>();

        await scheduler.StartAsync();
        context.Response.StatusCode = 200;
    }

    private static Task RestartTickerHost<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var scheduler = context.RequestServices.GetRequiredService<ITickerQHostScheduler>();

        scheduler.Restart();
        context.Response.StatusCode = 200;
        return Task.CompletedTask;
    }

    private static async Task GetTickerHostStatus<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var scheduler = context.RequestServices.GetRequiredService<ITickerQHostScheduler>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        await WriteJson(context, new HostStatusResponse { IsRunning = scheduler.IsRunning }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetLastWeekJobStatus<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var jobStatuses = await repository.GetLastWeekJobStatusesAsync(cancellationToken);
        await WriteJson(context, jobStatuses.Select(x => new TupleResponse<int, int> { Item1 = x.Item1, Item2 = x.Item2 }).ToArray(), dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetJobStatuses<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var jobStatuses = await repository.GetOverallJobStatusesAsync(cancellationToken);
        await WriteJson(context, jobStatuses.Select(x => new TupleResponse<TickerStatus, int> { Item1 = x.Item1, Item2 = x.Item2 }).ToArray(), dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetMachineJobs<TTimeTicker, TCronTicker>(HttpContext context)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var repository = context.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var cancellationToken = context.RequestAborted;

        var machineJobs = await repository.GetMachineJobsAsync(cancellationToken);
        await WriteJson(context, machineJobs.Select(x => new TupleResponse<string, int> { Item1 = x.Item1, Item2 = x.Item2 }).ToArray(), dashboardOptions.DashboardJsonOptions);
    }

    internal static string? ToIanaTimeZoneId(TimeZoneInfo? timeZone)
    {
        if (timeZone == null)
            return null;

        var id = timeZone.Id;

        // Already an IANA id (contains '/')
        if (id.Contains('/') || id == "UTC")
            return id;

        // Convert Windows timezone id to IANA
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
            return ianaId;

        // Fallback: return the original id
        return id;
    }

    #endregion
}
