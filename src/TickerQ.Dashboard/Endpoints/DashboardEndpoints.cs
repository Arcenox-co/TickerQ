using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Dashboard.Assistant;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Authentication.Endpoints;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
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
        // Auth endpoints (kept) — these are public + scoped outside the /api group.
        WithGroupNameIfSet(endpoints.MapGet("/api/auth/info", GetAuthInfo)
            .WithName("GetAuthInfo")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous(), config);

        WithGroupNameIfSet(endpoints.MapPost("/api/auth/validate", ValidateAuth)
            .WithName("ValidateAuth")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous(), config);

        WithGroupNameIfSet(endpoints.MapGet("/auth/challenge", (DashboardOptionsBuilder dashboardOptions) =>
            dashboardOptions.Auth.Mode == AuthMode.Host ? Results.Challenge() : Results.Unauthorized())
            .ExcludeFromDescription()
            .AllowAnonymous(), config);

        // Login / refresh / logout — only registered when a credential-issuing scheme is configured.
        endpoints.MapAuthEndpoints(config.Auth);

        var apiGroup = endpoints.MapGroup("/api").WithTags("TickerQ Dashboard").RequireCors("TickerQ_Dashboard_CORS");
        WithGroupNameIfSet(apiGroup, config);

        // Apply authentication if configured
        if (config.Auth.Mode == AuthMode.Host)
        {
            if (!string.IsNullOrEmpty(config.Auth.HostAuthorizationPolicy))
                apiGroup.RequireAuthorization(config.Auth.HostAuthorizationPolicy);
            else
                apiGroup.RequireAuthorization();
        }

        // Options (kept): React app reads these on boot for basePath/concurrency/tz.
        apiGroup.MapGet("/options", GetOptions);

        // AI assistant chat (read-only) — mapped only when configured. Sits
        // under /api so AuthMiddleware gates it; not under the write group.
        apiGroup.MapAssistantEndpoints<TTimeTicker, TCronTicker>(config);

        // ===== Reads — mirror DashboardService proto RPCs =====
        var d = apiGroup.MapGroup("/dashboard");

        d.MapPost("/time-tickers/query", QueryTimeTickers<TTimeTicker, TCronTicker>);
        d.MapGet("/time-tickers/{id:guid}", GetTimeTickerById<TTimeTicker, TCronTicker>);
        d.MapGet("/time-tickers/{id:guid}/children", GetTimeTickerChildren<TTimeTicker, TCronTicker>);
        d.MapGet("/time-tickers/{id:guid}/request", GetTimeTickerRequest<TTimeTicker, TCronTicker>);

        d.MapPost("/cron-tickers/query", QueryCronTickers<TTimeTicker, TCronTicker>);
        d.MapGet("/cron-tickers/{id:guid}", GetCronTickerById<TTimeTicker, TCronTicker>);

        d.MapPost("/cron-occurrences/{cronTickerId:guid}/query", QueryCronOccurrences<TTimeTicker, TCronTicker>);
        d.MapGet("/cron-occurrences/{id:guid}", GetCronOccurrenceById<TTimeTicker, TCronTicker>);
        d.MapGet("/cron-occurrences/{id:guid}/request", GetCronOccurrenceRequest<TTimeTicker, TCronTicker>);

        d.MapPost("/executions/query", QueryExecutions<TTimeTicker, TCronTicker>);

        d.MapGet("/stats/overall-statuses", GetOverallStatuses<TTimeTicker, TCronTicker>);
        d.MapGet("/stats/node-jobs", GetNodeJobs<TTimeTicker, TCronTicker>);

        d.MapGet("/overview/upcoming", GetUpcomingTickers<TTimeTicker, TCronTicker>);
        d.MapGet("/overview/recent-activity", GetRecentActivity<TTimeTicker, TCronTicker>);

        d.MapGet("/nodes", GetNodes<TTimeTicker, TCronTicker>);
        d.MapGet("/nodes/{nodeName}/functions", GetNodeFunctions<TTimeTicker, TCronTicker>);
        d.MapGet("/functions", GetAllFunctions<TTimeTicker, TCronTicker>);

        d.MapGet("/host/status", GetHostStatus<TTimeTicker, TCronTicker>);
        d.MapGet("/host/next-ticker", GetNextTicker<TTimeTicker, TCronTicker>);

        d.MapGet("/graphs/time-tickers", GetTimeTickersGraph<TTimeTicker, TCronTicker>);
        d.MapGet("/graphs/cron-tickers", GetCronTickersGraph<TTimeTicker, TCronTicker>);
        d.MapGet("/graphs/cron-tickers/{cronTickerId:guid}", GetCronTickerGraphById<TTimeTicker, TCronTicker>);
        d.MapGet("/graphs/cron-occurrences/{cronTickerId:guid}", GetCronOccurrencesGraph<TTimeTicker, TCronTicker>);

        d.MapGet("/log-tail/{tickerId:guid}", GetTickerLogTail);

        // ===== Writes — mirror DashboardOperationService proto RPCs =====
        // Registered on their own sub-group so read-only mode can reject the
        // whole set with 403. The SPA hides the affordances; this guard makes
        // the guarantee hold for hand-crafted requests too. Evaluated per
        // request so the predicate overload (role-based viewers) works.
        var w = d.MapGroup(string.Empty);
        if (config.ReadOnly || config.ReadOnlyPredicate != null)
        {
            w.AddEndpointFilter(async (invocationContext, next) =>
            {
                if (config.IsReadOnlyFor(invocationContext.HttpContext))
                    return Results.Text("Dashboard is running in read-only mode.", statusCode: StatusCodes.Status403Forbidden);
                return await next(invocationContext);
            });
        }

        w.MapPost("/host/start", StartHost<TTimeTicker, TCronTicker>);
        w.MapPost("/host/stop", StopHost<TTimeTicker, TCronTicker>);
        w.MapPost("/host/restart", RestartHost<TTimeTicker, TCronTicker>);

        w.MapPost("/tickers/{id:guid}/cancel", CancelTicker<TTimeTicker, TCronTicker>);
        w.MapPost("/tickers/bulk-cancel", BulkCancelTickers<TTimeTicker, TCronTicker>);
        w.MapPost("/time-tickers/bulk-delete", BulkDeleteTimeTickers<TTimeTicker, TCronTicker>);
        w.MapPost("/cron-tickers/bulk-delete", BulkDeleteCronTickers<TTimeTicker, TCronTicker>);
        w.MapPost("/executions/bulk-retry", BulkRetryExecutions<TTimeTicker, TCronTicker>);

        w.MapPost("/time-tickers", AddTimeTicker<TTimeTicker, TCronTicker>);
        w.MapDelete("/time-tickers/{id:guid}", DeleteTimeTicker<TTimeTicker, TCronTicker>);
        w.MapPost("/time-tickers/{id:guid}/run", RunTimeTickerOnDemand<TTimeTicker, TCronTicker>);
        w.MapPost("/time-tickers/{id:guid}/duplicate", DuplicateTimeTicker<TTimeTicker, TCronTicker>);
        w.MapPatch("/time-tickers/{id:guid}", UpdateTimeTicker<TTimeTicker, TCronTicker>);
        w.MapPost("/time-tickers/chain", AddTimeTickerChain<TTimeTicker, TCronTicker>);
        w.MapPut("/time-tickers/chain/{rootId:guid}", ReplaceTimeTickerChain<TTimeTicker, TCronTicker>);

        w.MapPost("/cron-tickers", AddCronTicker<TTimeTicker, TCronTicker>);
        w.MapPatch("/cron-tickers/{id:guid}", UpdateCronTicker<TTimeTicker, TCronTicker>);
        w.MapPost("/cron-tickers/{id:guid}/toggle", ToggleCronTicker<TTimeTicker, TCronTicker>);
        w.MapPost("/cron-tickers/{id:guid}/run", RunCronTickerOnDemand<TTimeTicker, TCronTicker>);
        w.MapDelete("/cron-tickers/{id:guid}", DeleteCronTicker<TTimeTicker, TCronTicker>);

        w.MapDelete("/cron-occurrences/{id:guid}", DeleteCronOccurrence<TTimeTicker, TCronTicker>);
    }

    // ===== Helpers =====

    private static IEndpointConventionBuilder WithGroupNameIfSet(IEndpointConventionBuilder builder, DashboardOptionsBuilder config)
    {
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            builder.WithGroupName(config.GroupName);
        return builder;
    }

    private static Task WriteJson<T>(HttpContext context, T value, JsonSerializerOptions options)
    {
        return Results.Json(value, options.GetTypeInfo(typeof(T))).ExecuteAsync(context);
    }

    /// <summary>
    /// Surface a failed <see cref="Utilities.Models.TickerResult{T}"/> to the
    /// client: validation errors are the caller's fault (400) and retain their
    /// actionable message. Unexpected server faults are logged and return only a
    /// generic message plus the request trace identifier.
    /// </summary>
    private static Task WriteTickerError(HttpContext context, Exception? exception)
    {
        if (exception is TickerValidatorException validationException)
        {
            context.Response.StatusCode = 400;
            return WriteJson(context, new ErrorResponseBody
            {
                Error = validationException.Message,
            }, Json(context));
        }

        context.RequestServices
            .GetService<ILoggerFactory>()?
            .CreateLogger(typeof(DashboardEndpoints).FullName!)
            .LogError(exception, "Dashboard ticker operation failed. TraceIdentifier: {TraceIdentifier}", context.TraceIdentifier);

        context.Response.StatusCode = 500;
        return WriteJson(context, new ErrorResponseBody
        {
            Error = $"An internal server error occurred. Reference: {context.TraceIdentifier}",
        }, Json(context));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context, JsonSerializerOptions options) where T : class
    {
        try
        {
            var typeInfo = options.GetTypeInfo(typeof(T));
            return await JsonSerializer.DeserializeAsync(context.Request.Body, typeInfo, context.RequestAborted) as T;
        }
        catch (JsonException)
        {
            // Malformed body (e.g. a request payload that isn't valid JSON) —
            // callers treat null as a 400, not a 500.
            return null;
        }
    }

    private static ITickerDashboardDataService<TTimeTicker, TCronTicker> DataService<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        => c.RequestServices.GetRequiredService<ITickerDashboardDataService<TTimeTicker, TCronTicker>>();

    private static ITickerDashboardRepository<TTimeTicker, TCronTicker> Repository<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        => c.RequestServices.GetRequiredService<ITickerDashboardRepository<TTimeTicker, TCronTicker>>();

    private static JsonSerializerOptions Json(HttpContext c)
        => c.RequestServices.GetRequiredService<DashboardOptionsBuilder>().DashboardJsonOptions;

    // ===== Auth =====

    private static async Task GetAuthInfo(HttpContext context)
    {
        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        var config = context.RequestServices.GetRequiredService<AuthConfig>();
        var info = authService.GetAuthInfo();

        var schemeNames = config.Schemes
            .Select(s => s.LegacyMode.ToString().ToLowerInvariant())
            .ToArray();
        var loginAvailable = config.Schemes
            .Any(s => s.LegacyMode is AuthMode.Jwt or AuthMode.Cookie);

        await WriteJson(context, new AuthInfoResponse
        {
            Mode = info.Mode.ToString().ToLower(),
            Enabled = info.IsEnabled,
            SessionTimeout = info.SessionTimeoutMinutes,
            Schemes = schemeNames,
            LoginAvailable = loginAvailable,
            LoginRedirect = config.HostLoginRedirectPath,
        }, Json(context));
    }

    private static async Task ValidateAuth(HttpContext context)
    {
        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var result = await authService.AuthenticateAsync(context);
        if (result.IsAuthenticated)
        {
            await WriteJson(context, new AuthValidateResponse
            {
                Authenticated = true,
                Username = result.Username,
                Message = "Authentication successful",
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

    // ===== Options =====

    private static async Task GetOptions(HttpContext context)
    {
        var executionContext = context.RequestServices.GetRequiredService<TickerExecutionContext>();
        var schedulerOptions = context.RequestServices.GetRequiredService<SchedulerOptionsBuilder>();
        await WriteJson(context, new DashboardOptionsResponse
        {
            MaxConcurrency = schedulerOptions.MaxConcurrency,
            IdleWorkerTimeOut = schedulerOptions.IdleWorkerTimeOut,
            CurrentMachine = schedulerOptions.NodeIdentifier,
            LastHostExceptionMessage = executionContext.LastHostExceptionMessage,
            SchedulerTimeZone = ToIanaTimeZoneId(schedulerOptions.SchedulerTimeZone),
        }, Json(context));
    }

    // ===== Reads =====

    private static async Task QueryTimeTickers<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var filter = await ReadJsonAsync<TimeTickerQueryFilter>(c, Json(c)) ?? new TimeTickerQueryFilter();
        var result = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickersFlatAsync(filter, c.RequestAborted);
        await WriteJson(c, result, Json(c));
    }

    private static async Task GetTimeTickerById<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var dto = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickerByIdAsync(id, c.RequestAborted);
        if (dto == null) { c.Response.StatusCode = 404; return; }
        await WriteJson(c, dto, Json(c));
    }

    private static async Task GetTimeTickerChildren<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var children = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickerChildrenAsync(id, c.RequestAborted);
        await WriteJson(c, children, Json(c));
    }

    private static async Task GetTimeTickerRequest<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var bytes = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickerRequestAsync(id, c.RequestAborted);
        await WriteJson(c, new TickerRequestPayloadResponse { Payload = bytes }, Json(c));
    }

    private static async Task QueryCronTickers<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var filter = await ReadJsonAsync<CronTickerQueryFilter>(c, Json(c)) ?? new CronTickerQueryFilter();
        var result = await DataService<TTimeTicker, TCronTicker>(c).GetCronTickersFlatAsync(filter, c.RequestAborted);
        await WriteJson(c, result, Json(c));
    }

    private static async Task GetCronTickerById<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var dto = await DataService<TTimeTicker, TCronTicker>(c).GetCronTickerByIdAsync(id, c.RequestAborted);
        if (dto == null) { c.Response.StatusCode = 404; return; }
        await WriteJson(c, dto, Json(c));
    }

    private static async Task QueryCronOccurrences<TTimeTicker, TCronTicker>(HttpContext c, Guid cronTickerId)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var filter = await ReadJsonAsync<CronOccurrenceQueryFilter>(c, Json(c)) ?? new CronOccurrenceQueryFilter();
        var result = await DataService<TTimeTicker, TCronTicker>(c).GetCronOccurrencesFlatAsync(cronTickerId, filter, c.RequestAborted);
        await WriteJson(c, result, Json(c));
    }

    private static async Task GetCronOccurrenceById<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var dto = await DataService<TTimeTicker, TCronTicker>(c).GetCronOccurrenceByIdAsync(id, c.RequestAborted);
        if (dto == null) { c.Response.StatusCode = 404; return; }
        await WriteJson(c, dto, Json(c));
    }

    private static async Task GetCronOccurrenceRequest<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var bytes = await DataService<TTimeTicker, TCronTicker>(c).GetCronOccurrenceRequestAsync(id, c.RequestAborted);
        await WriteJson(c, new TickerRequestPayloadResponse { Payload = bytes }, Json(c));
    }

    private static async Task QueryExecutions<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var filter = await ReadJsonAsync<ExecutionQueryFilter>(c, Json(c)) ?? new ExecutionQueryFilter();
        var result = await DataService<TTimeTicker, TCronTicker>(c).GetExecutionsFlatAsync(filter, c.RequestAborted);
        await WriteJson(c, result, Json(c));
    }

    private static async Task GetOverallStatuses<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetOverallStatusesAsync(c.RequestAborted);
        IList<StatusCountResponseBody> body = data
            .Select(x => new StatusCountResponseBody { Status = x.Status, Count = x.Count })
            .ToList();
        await WriteJson(c, body, Json(c));
    }

    private static async Task GetNodeJobs<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetNodeJobsAsync(c.RequestAborted);
        IList<NodeJobCountResponseBody> body = data
            .Select(x => new NodeJobCountResponseBody { NodeName = x.NodeName ?? string.Empty, JobCount = x.JobCount })
            .ToList();
        await WriteJson(c, body, Json(c));
    }

    private static async Task GetUpcomingTickers<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var count = int.TryParse(c.Request.Query["count"], out var n) ? n : 10;
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetUpcomingTickersAsync(count, c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetRecentActivity<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var count = int.TryParse(c.Request.Query["count"], out var n) ? n : 10;
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetRecentActivityAsync(count, c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetNodes<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetNodesAsync(c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetNodeFunctions<TTimeTicker, TCronTicker>(HttpContext c, string nodeName)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetNodeFunctionsAsync(nodeName, c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetAllFunctions<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetAllFunctionsAsync(c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetHostStatus<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var dto = await DataService<TTimeTicker, TCronTicker>(c).GetHostStatusAsync(c.RequestAborted);
        await WriteJson(c, dto, Json(c));
    }

    private static async Task GetNextTicker<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var dto = await DataService<TTimeTicker, TCronTicker>(c).GetNextTickerAsync(c.RequestAborted);
        await WriteJson(c, dto, Json(c));
    }

    private static async Task GetTimeTickersGraph<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var (past, future) = ParseGraphRange(c);
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickersGraphAsync(past, future, c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetCronTickersGraph<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var (past, future) = ParseGraphRange(c);
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetCronTickersGraphAsync(past, future, c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetCronTickerGraphById<TTimeTicker, TCronTicker>(HttpContext c, Guid cronTickerId)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var (past, future) = ParseGraphRange(c);
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetCronTickerGraphByIdAsync(cronTickerId, past, future, c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static async Task GetCronOccurrencesGraph<TTimeTicker, TCronTicker>(HttpContext c, Guid cronTickerId)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var data = await DataService<TTimeTicker, TCronTicker>(c).GetCronOccurrencesGraphAsync(cronTickerId, c.RequestAborted);
        await WriteJson(c, data, Json(c));
    }

    private static (int Past, int Future) ParseGraphRange(HttpContext c)
    {
        var past = int.TryParse(c.Request.Query["pastDays"], out var p) ? p : 7;
        var future = int.TryParse(c.Request.Query["futureDays"], out var f) ? f : 0;
        return (past, future);
    }

    // Log tail — served from the in-process capture store (ILogger lines emitted
    // inside a ticker execution, kept in bounded per-ticker buffers for ~30 min).
    // The store is registered by AddTickerQ; tolerate its absence so the panel
    // renders the "no logs" state instead of 500.
    private static Task GetTickerLogTail(HttpContext c, Guid tickerId)
    {
        var body = new TickerLogTailResponseBody();

        if (c.RequestServices.GetService<ITickerExecutionLogStore>() is { } store)
        {
            foreach (var line in store.GetTail(tickerId))
            {
                body.Lines.Add(new TickerLogLineResponse
                {
                    UnixMs = line.UnixMs,
                    Level = line.Level,
                    Source = string.Empty,
                    Message = line.Message,
                    Category = line.Category,
                    FunctionName = line.FunctionName,
                });
            }
        }

        return WriteJson(c, body, Json(c));
    }

    // ===== Writes =====

    private static async Task StartHost<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await DataService<TTimeTicker, TCronTicker>(c).StartHostAsync(c.RequestAborted);
        c.Response.StatusCode = 204;
    }

    private static async Task StopHost<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await DataService<TTimeTicker, TCronTicker>(c).StopHostAsync(c.RequestAborted);
        c.Response.StatusCode = 204;
    }

    private static Task RestartHost<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        DataService<TTimeTicker, TCronTicker>(c).RestartHost();
        c.Response.StatusCode = 204;
        return Task.CompletedTask;
    }

    private static Task CancelTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var ok = DataService<TTimeTicker, TCronTicker>(c).CancelTicker(id);
        c.Response.StatusCode = ok ? 204 : 404;
        return Task.CompletedTask;
    }

    private static async Task AddTimeTicker<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<AddTimeTickerRequest>(c, Json(c));
        if (body == null || string.IsNullOrWhiteSpace(body.Function))
        {
            c.Response.StatusCode = 400;
            return;
        }

        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var entity = new TTimeTicker
        {
            Function = body.Function,
            ExecutionTime = body.ExecutionTime,
            Description = body.Description,
            Retries = body.Retries ?? 0,
            Request = body.Request,
            RetryIntervals = body.RetryIntervalsSeconds,
            OnStale = body.OnStale ?? StaleAction.Restart,
            TimeoutSeconds = body.TimeoutSeconds is > 0 ? body.TimeoutSeconds : null,
        };

        var result = await manager.AddAsync(entity, c.RequestAborted);
        if (!result.IsSucceeded || result.Result == null)
        {
            await WriteTickerError(c, result.Exception);
            return;
        }
        await WriteJson(c, new AddTickerResponseBody { Id = result.Result.Id.ToString() }, Json(c));
    }

    private static async Task DeleteTimeTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var result = await manager.DeleteAsync(id, c.RequestAborted);
        c.Response.StatusCode = result.IsSucceeded ? 204 : 404;
    }

    // ===== Bulk operations (multi-select on the dashboard tables) =====

    private static async Task BulkDeleteTimeTickers<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<BulkIdsRequest>(c, Json(c));
        if (body == null || body.Ids.Count == 0) { c.Response.StatusCode = 400; return; }

        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var result = await manager.DeleteBatchAsync(body.Ids, c.RequestAborted);
        await WriteJson(c, new BulkActionResponseBody { Affected = result.AffectedRows }, Json(c));
    }

    private static async Task BulkDeleteCronTickers<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<BulkIdsRequest>(c, Json(c));
        if (body == null || body.Ids.Count == 0) { c.Response.StatusCode = 400; return; }

        var manager = c.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var result = await manager.DeleteBatchAsync(body.Ids, c.RequestAborted);
        await WriteJson(c, new BulkActionResponseBody { Affected = result.AffectedRows }, Json(c));
    }

    private static async Task BulkCancelTickers<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<BulkIdsRequest>(c, Json(c));
        if (body == null || body.Ids.Count == 0) { c.Response.StatusCode = 400; return; }

        var dataService = DataService<TTimeTicker, TCronTicker>(c);
        var cancelled = 0;
        foreach (var id in body.Ids)
        {
            if (dataService.CancelTicker(id))
                cancelled++;
        }

        await WriteJson(c, new BulkActionResponseBody { Affected = cancelled }, Json(c));
    }

    private static async Task BulkRetryExecutions<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<BulkRetryRequest>(c, Json(c));
        if (body == null || body.Items.Count == 0) { c.Response.StatusCode = 400; return; }

        var repository = Repository<TTimeTicker, TCronTicker>(c);
        var affected = 0;

        // Avoid firing the same cron more than once if several of its occurrences are selected.
        var cronIdsTriggered = new HashSet<Guid>();
        foreach (var item in body.Items)
        {
            if (string.Equals(item.Type, "TimeTicker", StringComparison.OrdinalIgnoreCase))
            {
                if (await repository.RunTimeTickerOnDemandAsync(item.Id, c.RequestAborted))
                    affected++;
            }
            else if (item.CronTickerId is { } cronId && cronIdsTriggered.Add(cronId))
            {
                await repository.AddOnDemandCronTickerOccurrenceAsync(cronId, c.RequestAborted);
                affected++;
            }
        }

        await WriteJson(c, new BulkActionResponseBody { Affected = affected }, Json(c));
    }

    private static async Task RunTimeTickerOnDemand<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var entity = await persistence.GetTimeTickerById(id, c.RequestAborted);
        if (entity == null) { c.Response.StatusCode = 404; return; }
        if (entity.Status == TickerStatus.InProgress) { c.Response.StatusCode = 409; return; }
        if (!await Repository<TTimeTicker, TCronTicker>(c)
                .RunTimeTickerOnDemandAsync(id, c.RequestAborted))
        {
            c.Response.StatusCode = 409;
            return;
        }
        c.Response.StatusCode = 204;
    }

    private static async Task UpdateTimeTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<UpdateTimeTickerRequest>(c, Json(c));
        if (body == null) { c.Response.StatusCode = 400; return; }

        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();

        var entity = await persistence.GetTimeTickerById(id, c.RequestAborted);
        if (entity == null) { c.Response.StatusCode = 404; return; }

        // Match the gRPC operation service: only edit while Idle/Queued.
        if (entity.Status != TickerStatus.Idle && entity.Status != TickerStatus.Queued)
        {
            c.Response.StatusCode = 409;
            return;
        }

        if (body.ExecutionTime.HasValue)            entity.ExecutionTime  = body.ExecutionTime.Value;
        if (body.Description != null)               entity.Description    = body.Description;
        if (body.Retries.HasValue)                  entity.Retries        = body.Retries.Value;
        if (body.RetryIntervalsSeconds != null)     entity.RetryIntervals = body.RetryIntervalsSeconds.Length > 0 ? body.RetryIntervalsSeconds : null;
        if (body.Request != null)                   entity.Request        = body.Request.Length > 0 ? body.Request : null;
        if (!string.IsNullOrWhiteSpace(body.Function)) entity.Function    = body.Function;
        if (body.RunCondition.HasValue)             entity.RunCondition   = body.RunCondition.Value;
        if (body.OnStale.HasValue)                  entity.OnStale        = body.OnStale.Value;
        if (body.TimeoutSeconds.HasValue)           entity.TimeoutSeconds = body.TimeoutSeconds.Value > 0 ? body.TimeoutSeconds.Value : null;

        var result = await manager.UpdateAsync(entity, c.RequestAborted);
        if (!result.IsSucceeded) { await WriteTickerError(c, result.Exception); return; }
        c.Response.StatusCode = 204;
    }

    private static async Task AddTimeTickerChain<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<AddTimeTickerChainRequest>(c, Json(c));
        if (body == null || body.Root == null)
        {
            c.Response.StatusCode = 400;
            return;
        }

        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var rootEntity = BuildChainEntity<TTimeTicker>(body.Root, isRoot: true, parentId: null);
        rootEntity.ExecutionTime = body.ExecutionTime;

        var createdCount = 1 + CountDescendants(rootEntity);
        var result = await manager.AddAsync(rootEntity, c.RequestAborted);
        if (!result.IsSucceeded || result.Result == null)
        {
            await WriteTickerError(c, result.Exception);
            return;
        }

        await WriteJson(c, new AddTimeTickerChainResponseBody
        {
            RootId = result.Result.Id.ToString(),
            CreatedCount = createdCount,
        }, Json(c));
    }

    /// <summary>
    /// Atomically replaces the whole chain aggregate rooted at <paramref name="rootId"/> with the
    /// posted tree. Server-side, this validates and persists the complete replacement before
    /// removing the original, in a single transaction — so a failed edit never deletes the
    /// original chain. The dashboard uses this instead of a delete-then-create sequence.
    /// </summary>
    private static async Task ReplaceTimeTickerChain<TTimeTicker, TCronTicker>(HttpContext c, Guid rootId)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<AddTimeTickerChainRequest>(c, Json(c));
        if (body == null || body.Root == null)
        {
            c.Response.StatusCode = 400;
            return;
        }

        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var newRoot = BuildChainEntity<TTimeTicker>(body.Root, isRoot: true, parentId: null);
        newRoot.ExecutionTime = body.ExecutionTime;

        var createdCount = 1 + CountDescendants(newRoot);
        var result = await manager.ReplaceChainAsync(rootId, newRoot, c.RequestAborted);
        if (!result.IsSucceeded || result.Result == null)
        {
            await WriteTickerError(c, result.Exception);
            return;
        }

        await WriteJson(c, new AddTimeTickerChainResponseBody
        {
            RootId = result.Result.Id.ToString(),
            CreatedCount = createdCount,
        }, Json(c));
    }

    private static async Task DuplicateTimeTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = c.Request.ContentLength is > 0
            ? await ReadJsonAsync<DuplicateTimeTickerRequest>(c, Json(c))
            : null;

        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();

        var copy = await CloneSubtreeAsync(persistence, id, c.RequestAborted);
        if (copy == null) { c.Response.StatusCode = 404; return; }

        copy.ExecutionTime = body?.ExecutionTime ?? DateTime.UtcNow;

        var added = await manager.AddAsync(copy, c.RequestAborted);
        if (!added.IsSucceeded || added.Result == null)
        {
            await WriteTickerError(c, added.Exception);
            return;
        }
        await WriteJson(c, new AddTickerResponseBody { Id = added.Result.Id.ToString() }, Json(c));
    }

    private static async Task AddCronTicker<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<AddCronTickerRequest>(c, Json(c));
        if (body == null || string.IsNullOrWhiteSpace(body.Function) || string.IsNullOrWhiteSpace(body.Expression))
        {
            c.Response.StatusCode = 400;
            return;
        }

        var manager = c.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var entity = new TCronTicker
        {
            Function = body.Function,
            Expression = body.Expression,
            Description = body.Description,
            Retries = body.Retries ?? 0,
            Request = body.Request,
            RetryIntervals = body.RetryIntervalsSeconds,
            IsEnabled = body.IsEnabled ?? true,
            OnStale = body.OnStale ?? StaleAction.Restart,
            TimeoutSeconds = body.TimeoutSeconds is > 0 ? body.TimeoutSeconds : null,
        };

        var result = await manager.AddAsync(entity, c.RequestAborted);
        if (!result.IsSucceeded || result.Result == null)
        {
            await WriteTickerError(c, result.Exception);
            return;
        }
        await WriteJson(c, new AddTickerResponseBody { Id = result.Result.Id.ToString() }, Json(c));
    }

    private static async Task UpdateCronTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<UpdateCronTickerRequestApi>(c, Json(c));
        if (body == null) { c.Response.StatusCode = 400; return; }

        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var manager = c.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();

        var entity = await persistence.GetCronTickerById(id, c.RequestAborted);
        if (entity == null) { c.Response.StatusCode = 404; return; }

        if (!string.IsNullOrWhiteSpace(body.Function))     entity.Function       = body.Function;
        if (!string.IsNullOrWhiteSpace(body.Expression))   entity.Expression     = body.Expression;
        if (body.Description != null)                      entity.Description    = body.Description;
        if (body.Retries.HasValue)                         entity.Retries        = body.Retries.Value;
        if (body.RetryIntervalsSeconds != null)            entity.RetryIntervals = body.RetryIntervalsSeconds.Length > 0 ? body.RetryIntervalsSeconds : null;
        if (body.Request != null)                          entity.Request        = body.Request.Length > 0 ? body.Request : null;
        if (body.IsEnabled.HasValue)                       entity.IsEnabled      = body.IsEnabled.Value;
        if (body.OnStale.HasValue)                         entity.OnStale        = body.OnStale.Value;
        if (body.TimeoutSeconds.HasValue)                  entity.TimeoutSeconds = body.TimeoutSeconds.Value > 0 ? body.TimeoutSeconds.Value : null;

        var result = await manager.UpdateAsync(entity, c.RequestAborted);
        if (!result.IsSucceeded) { await WriteTickerError(c, result.Exception); return; }
        c.Response.StatusCode = 204;
    }

    private static async Task ToggleCronTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<ToggleCronTickerBody>(c, Json(c));
        if (body == null) { c.Response.StatusCode = 400; return; }
        var ok = await DataService<TTimeTicker, TCronTicker>(c).ToggleCronTickerAsync(id, body.IsEnabled, c.RequestAborted);
        c.Response.StatusCode = ok ? 204 : 404;
    }

    private static async Task RunCronTickerOnDemand<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await DataService<TTimeTicker, TCronTicker>(c).RunCronTickerOnDemandAsync(id, c.RequestAborted);
        c.Response.StatusCode = 204;
    }

    private static async Task DeleteCronTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var manager = c.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var result = await manager.DeleteAsync(id, c.RequestAborted);
        c.Response.StatusCode = result.IsSucceeded ? 204 : 404;
    }

    private static async Task DeleteCronOccurrence<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var removed = await persistence.RemoveCronTickerOccurrences(new[] { id }, c.RequestAborted);
        c.Response.StatusCode = removed > 0 ? 204 : 404;
    }

    // ===== Chain helpers (ported from DashboardOperationGrpcService) =====

    private static TTimeTicker BuildChainEntity<TTimeTicker>(TimeTickerNodeRequest node, bool isRoot, Guid? parentId)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        var entity = new TTimeTicker
        {
            Id = Guid.NewGuid(),
            Function = node.Function,
            Description = node.Description,
            Retries = node.Retries ?? 0,
            Request = node.Request,
            RetryIntervals = node.RetryIntervalsSeconds,
            OnStale = node.OnStale ?? StaleAction.Restart,
            TimeoutSeconds = node.TimeoutSeconds is > 0 ? node.TimeoutSeconds : null,
        };

        if (!isRoot)
        {
            entity.ParentId = parentId;
            if (node.RunCondition.HasValue)
                entity.RunCondition = node.RunCondition.Value;
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var childEntity = BuildChainEntity<TTimeTicker>(child, isRoot: false, parentId: entity.Id);
                entity.Children.Add(childEntity);
            }
        }
        return entity;
    }

    private static int CountDescendants<TTimeTicker>(TTimeTicker entity)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        var count = entity.Children.Count;
        foreach (var child in entity.Children)
            count += CountDescendants(child);
        return count;
    }

    private static async Task<TTimeTicker?> CloneSubtreeAsync<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence, Guid sourceId, System.Threading.CancellationToken ct)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var src = await persistence.GetTimeTickerById(sourceId, ct);
        if (src == null) return null;

        var copy = new TTimeTicker
        {
            Id = Guid.NewGuid(),
            Function = src.Function,
            Description = src.Description,
            Retries = src.Retries,
            RetryIntervals = src.RetryIntervals,
            Request = src.Request,
            RunCondition = src.RunCondition,
            OnStale = src.OnStale,
            TimeoutSeconds = src.TimeoutSeconds,
        };

        foreach (var child in src.Children)
        {
            var childCopy = await CloneSubtreeAsync(persistence, child.Id, ct);
            if (childCopy == null) continue;
            childCopy.ParentId = copy.Id;
            copy.Children.Add(childCopy);
        }
        return copy;
    }

    // ===== Time zone helper (kept from original) =====

    internal static string? ToIanaTimeZoneId(TimeZoneInfo? timeZone)
    {
        if (timeZone == null) return null;
        var id = timeZone.Id;
        if (id.Contains('/') || id == "UTC") return id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId)) return ianaId;
        return id;
    }
}
