using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints<TTimeTicker, TCronTicker>(this IEndpointRouteBuilder endpoints, DashboardOptionsBuilder config)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        // Auth endpoints (kept) — these are public + scoped outside the /api group.
        WithGroupNameIfSet(endpoints.MapGetAot("/api/auth/info", GetAuthInfo)
            .WithName("GetAuthInfo")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous(), config);

        WithGroupNameIfSet(endpoints.MapPostAot("/api/auth/validate", ValidateAuth)
            .WithName("ValidateAuth")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous(), config);

        WithGroupNameIfSet(endpoints.MapGetResultAot("/auth/challenge", context =>
            Task.FromResult<IResult>(context.RequestServices.GetRequiredService<DashboardOptionsBuilder>().Auth.Mode == AuthMode.Host
                ? Results.Challenge()
                : Results.Unauthorized()))
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
        apiGroup.MapGetAot("/options", GetOptions);

        // Read-only license status for the About page and the global banner. Authenticated exactly like
        // every other read (it sits under /api). Exposes only safe, presentation-ready fields.
        apiGroup.MapGetAot("/license", GetLicense);

        // AI assistant — mapped only when configured. Sits under /api so
        // AuthMiddleware gates it; its mutation handlers enforce read-only access.
        apiGroup.MapAssistantEndpoints<TTimeTicker, TCronTicker>(config);

        // ===== Reads — mirror DashboardService proto RPCs =====
        var d = apiGroup.MapGroup("/dashboard");

        d.MapPostAot("/time-tickers/query", QueryTimeTickers<TTimeTicker, TCronTicker>);
        d.MapGetAot("/time-tickers/{id:guid}", c => GetTimeTickerById<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/time-tickers/{id:guid}/chain", c => GetTimeTickerChain<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/time-tickers/{id:guid}/children", c => GetTimeTickerChildren<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/time-tickers/{id:guid}/request", c => GetTimeTickerRequest<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/time-tickers/{id:guid}/result", c => GetTimeTickerResult<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));

        d.MapPostAot("/cron-tickers/query", QueryCronTickers<TTimeTicker, TCronTicker>);
        d.MapGetAot("/cron-tickers/{id:guid}", c => GetCronTickerById<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));

        d.MapPostAot("/cron-occurrences/{cronTickerId:guid}/query", c => QueryCronOccurrences<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("cronTickerId")));
        d.MapGetAot("/cron-occurrences/{id:guid}", c => GetCronOccurrenceById<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/cron-tickers/{id:guid}/request", c => GetCronTickerRequest<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/cron-occurrences/{id:guid}/request", c => GetCronOccurrenceRequest<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));

        d.MapPostAot("/executions/query", QueryExecutions<TTimeTicker, TCronTicker>);

        d.MapGetAot("/stats/overall-statuses", GetOverallStatuses<TTimeTicker, TCronTicker>);
        d.MapGetAot("/stats/node-jobs", GetNodeJobs<TTimeTicker, TCronTicker>);

        d.MapGetAot("/overview/upcoming", GetUpcomingTickers<TTimeTicker, TCronTicker>);
        d.MapGetAot("/overview/recent-activity", GetRecentActivity<TTimeTicker, TCronTicker>);

        d.MapGetAot("/nodes", GetNodes<TTimeTicker, TCronTicker>);
        d.MapGetAot("/nodes/{nodeName}/functions", c => GetNodeFunctions<TTimeTicker, TCronTicker>(c, c.RequiredRouteString("nodeName")));
        d.MapGetAot("/functions", GetAllFunctions<TTimeTicker, TCronTicker>);
        d.MapGetAot("/functions/test-run-chain/{id:guid}", c => GetTestRunChain(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/definitions", ListDefinitions<TTimeTicker, TCronTicker>);
        d.MapGetAot("/definitions/{id:guid}", c => GetDefinition<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/schedule-policies", ListSchedulePolicies);
        d.MapGetAot("/schedule-policies/date-providers", ListScheduleDateProviders);
        d.MapGetAot("/schedule-policies/{id:guid}", c => GetSchedulePolicy(c, c.RequiredRouteGuid("id")));
        d.MapPostAot("/schedule-policies/{id:guid}/preview", c => PreviewSchedulePolicy(c, c.RequiredRouteGuid("id")));
        d.MapGetAot("/schedule-policies/{id:guid}/usage", c => GetSchedulePolicyUsage<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));

        d.MapGetAot("/host/status", GetHostStatus<TTimeTicker, TCronTicker>);
        d.MapGetAot("/host/next-ticker", GetNextTicker<TTimeTicker, TCronTicker>);

        d.MapGetAot("/graphs/time-tickers", GetTimeTickersGraph<TTimeTicker, TCronTicker>);
        d.MapGetAot("/graphs/cron-tickers", GetCronTickersGraph<TTimeTicker, TCronTicker>);
        d.MapGetAot("/graphs/cron-tickers/{cronTickerId:guid}", c => GetCronTickerGraphById<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("cronTickerId")));
        d.MapGetAot("/graphs/cron-occurrences/{cronTickerId:guid}", c => GetCronOccurrencesGraph<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("cronTickerId")));

        d.MapGetAot("/log-tail/{tickerId:guid}", c => GetTickerLogTail(c, c.RequiredRouteGuid("tickerId")));
        d.MapPostAot("/log-tail/query", GetTickerLogBatch);
        d.MapGetAot("/diagnostics", GetRuntimeDiagnostics);

        // ===== Writes — mirror DashboardOperationService proto RPCs =====
        // Registered on their own sub-group so read-only mode can reject the
        // whole set with 403. The SPA hides the affordances; this guard makes
        // the guarantee hold for hand-crafted requests too. Evaluated per
        // request so the predicate overload (role-based viewers) works.
        var w = d.MapGroup(string.Empty);

        // License enforcement: when the offline license blocks execution, no mutation may run. Evaluated
        // per request, before every write handler, and returned as safe JSON with the license reason.
        // Reads and GET /api/license stay available for diagnosis.
        w.AddEndpointFilter(async (invocationContext, next) =>
        {
            var license = invocationContext.HttpContext.RequestServices
                .GetRequiredService<TickerQLicenseStateProvider>().Current;
            if (!license.ExecutionAllowed)
                return Results.Json(
                    new ErrorResponseBody { Error = license.Message },
                    DashboardJsonSerializerContext.Default.ErrorResponseBody,
                    statusCode: StatusCodes.Status403Forbidden);
            return await next(invocationContext);
        });

        if (config.ReadOnly || config.ReadOnlyPredicate != null)
        {
            w.AddEndpointFilter(async (invocationContext, next) =>
            {
                if (config.IsReadOnlyFor(invocationContext.HttpContext))
                    return Results.Text("Dashboard is running in read-only mode.", statusCode: StatusCodes.Status403Forbidden);
                return await next(invocationContext);
            });
        }

        d.MapIntegrations(w, config);

        w.MapPostAot("/host/start", StartHost<TTimeTicker, TCronTicker>);
        w.MapPostAot("/host/stop", StopHost<TTimeTicker, TCronTicker>);
        w.MapPostAot("/host/restart", RestartHost<TTimeTicker, TCronTicker>);

        w.MapPostAot("/tickers/{id:guid}/cancel", c => CancelTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/tickers/bulk-cancel", BulkCancelTickers<TTimeTicker, TCronTicker>);
        w.MapPostAot("/time-tickers/{id:guid}/cancel", c => CancelPendingTimeTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/time-tickers/{id:guid}/request-cancellation", c => RequestTimeTickerCancellation<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/cron-occurrences/{id:guid}/request-cancellation", c => RequestCronOccurrenceCancellation<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/time-tickers/bulk-delete", BulkDeleteTimeTickers<TTimeTicker, TCronTicker>);
        w.MapPostAot("/cron-tickers/bulk-delete", BulkDeleteCronTickers<TTimeTicker, TCronTicker>);
        w.MapPostAot("/executions/bulk-retry", BulkRetryExecutions<TTimeTicker, TCronTicker>);
        w.MapPostAot("/functions/test-run", TestRunFunction);
        w.MapPostAot("/functions/test-run-chain", TestRunChain);
        w.MapPostAot("/definitions", CreateDefinition<TTimeTicker, TCronTicker>);
        w.MapPutAot("/definitions/{id:guid}", c => UpdateDefinition<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapDeleteAot("/definitions/{id:guid}", c => DeleteDefinition<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/definitions/from-time-ticker/{id:guid}", c => CreateDefinitionFromTimeTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/definitions/from-cron-ticker/{id:guid}", c => CreateDefinitionFromCronTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/definitions/{id:guid}/activate", c => ActivateDraft<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/schedule-policies", CreateSchedulePolicy);
        w.MapPutAot("/schedule-policies/{id:guid}", c => UpdateSchedulePolicy(c, c.RequiredRouteGuid("id")));
        w.MapDeleteAot("/schedule-policies/{id:guid}", c => DeleteSchedulePolicy(c, c.RequiredRouteGuid("id")));

        w.MapPostAot("/time-tickers", AddTimeTicker<TTimeTicker, TCronTicker>);
        w.MapDeleteAot("/time-tickers/{id:guid}", c => DeleteTimeTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/time-tickers/{id:guid}/run", c => RunTimeTickerOnDemand<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/time-tickers/{id:guid}/retry-from-here", c => RetryTimeTickerFromHere<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/time-tickers/{id:guid}/duplicate", c => DuplicateTimeTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPatchAot("/time-tickers/{id:guid}", c => UpdateTimeTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPatchAot("/time-tickers/{id:guid}/delete-behavior", c => SetChainDeleteBehavior<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/time-tickers/chain", AddTimeTickerChain<TTimeTicker, TCronTicker>);
        w.MapPutAot("/time-tickers/chain/{rootId:guid}", c => ReplaceTimeTickerChain<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("rootId")));

        w.MapPostAot("/cron-tickers", AddCronTicker<TTimeTicker, TCronTicker>);
        w.MapPatchAot("/cron-tickers/{id:guid}", c => UpdateCronTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/cron-tickers/{id:guid}/toggle", c => ToggleCronTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/cron-tickers/{id:guid}/run", c => RunCronTickerOnDemand<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapPostAot("/cron-occurrences/{id:guid}/run", c => RerunCronOccurrence<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
        w.MapDeleteAot("/cron-tickers/{id:guid}", c => DeleteCronTicker<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));

        w.MapDeleteAot("/cron-occurrences/{id:guid}", c => DeleteCronOccurrence<TTimeTicker, TCronTicker>(c, c.RequiredRouteGuid("id")));
    }

    // ===== Helpers =====

    private static IEndpointConventionBuilder WithGroupNameIfSet(IEndpointConventionBuilder builder, DashboardOptionsBuilder config)
    {
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            builder.WithGroupName(config.GroupName);
        return builder;
    }

    private static async Task ListSchedulePolicies(HttpContext c)
    {
        var policies = await c.RequestServices.GetRequiredService<ISchedulePolicyManager>()
            .ListAsync(c.RequestAborted).ConfigureAwait(false);
        await WriteJson(c, policies.ToList(), Json(c));
    }

    private static async Task ListScheduleDateProviders(HttpContext c)
    {
        var names = c.RequestServices.GetServices<IScheduleDateProvider>()
            .Select(static provider => provider.Name?.Trim())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await WriteJson(c, new ScheduleDateProvidersResponse { Names = names }, Json(c));
    }

    private static async Task GetSchedulePolicy(HttpContext c, Guid id)
    {
        var policy = await c.RequestServices.GetRequiredService<ISchedulePolicyManager>()
            .GetAsync(id, c.RequestAborted).ConfigureAwait(false);
        if (policy == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        await WriteJson(c, policy, Json(c));
    }

    private static async Task PreviewSchedulePolicy(HttpContext c, Guid id)
    {
        var body = await ReadJsonAsync<SchedulePolicyPreviewRequest>(c, Json(c));
        if (body == null || string.IsNullOrWhiteSpace(body.Expression))
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var manager = c.RequestServices.GetRequiredService<ISchedulePolicyManager>();
        var policy = await manager.GetAsync(id, c.RequestAborted).ConfigureAwait(false);
        if (policy == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        try
        {
            var preview = await manager.PreviewCronAsync(policy.Name, body.Expression,
                body.FromUtc == default ? DateTime.UtcNow : body.FromUtc,
                body.Count, body.TimeZoneId, c.RequestAborted).ConfigureAwait(false);
            await WriteJson(c, preview.ToList(), Json(c));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody { Error = exception.Message }, Json(c));
        }
    }

    private static async Task GetSchedulePolicyUsage<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var policy = await c.RequestServices.GetRequiredService<ISchedulePolicyManager>()
            .GetAsync(id, c.RequestAborted).ConfigureAwait(false);
        if (policy == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var store = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var timeCountTask = store.TimeTickersQuery().Where(x => x.SchedulePolicyName == policy.Name)
            .CountAsync(c.RequestAborted);
        var cronCountTask = store.CronTickersQuery().Where(x => x.SchedulePolicyName == policy.Name)
            .CountAsync(c.RequestAborted);
        await Task.WhenAll(timeCountTask, cronCountTask).ConfigureAwait(false);
        await WriteJson(c, new SchedulePolicyUsageResponse
        {
            TimeTickerCount = await timeCountTask.ConfigureAwait(false),
            CronTickerCount = await cronCountTask.ConfigureAwait(false),
        }, Json(c));
    }

    private static async Task CreateSchedulePolicy(HttpContext c)
    {
        var body = await ReadJsonAsync<SaveSchedulePolicyRequest>(c, Json(c));
        if (body == null) { c.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        try
        {
            var created = await c.RequestServices.GetRequiredService<ISchedulePolicyManager>()
                .CreateAsync(ToSchedulePolicy(body), c.RequestAborted).ConfigureAwait(false);
            c.Response.StatusCode = StatusCodes.Status201Created;
            await WriteJson(c, created, Json(c));
        }
        catch (Exception exception) when (exception is TickerValidatorException or DuplicateSchedulePolicyNameException)
        {
            c.Response.StatusCode = exception is DuplicateSchedulePolicyNameException
                ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody { Error = exception.Message }, Json(c));
        }
    }

    private static async Task UpdateSchedulePolicy(HttpContext c, Guid id)
    {
        var body = await ReadJsonAsync<UpdateSchedulePolicyRequest>(c, Json(c));
        if (body == null) { c.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        try
        {
            var policy = ToSchedulePolicy(body);
            policy.Id = id;
            var updated = await c.RequestServices.GetRequiredService<ISchedulePolicyManager>()
                .UpdateAsync(policy, body.Version, c.RequestAborted).ConfigureAwait(false);
            if (updated == null)
            {
                c.Response.StatusCode = StatusCodes.Status409Conflict;
                await WriteJson(c, new ErrorResponseBody
                {
                    Error = "The schedule policy changed. Reload it before saving."
                }, Json(c));
                return;
            }
            await WriteJson(c, updated, Json(c));
        }
        catch (Exception exception) when (exception is TickerValidatorException or DuplicateSchedulePolicyNameException)
        {
            c.Response.StatusCode = exception is DuplicateSchedulePolicyNameException
                ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody { Error = exception.Message }, Json(c));
        }
    }

    private static async Task DeleteSchedulePolicy(HttpContext c, Guid id)
    {
        if (!long.TryParse(c.Request.Query["version"], out var version) || version <= 0)
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody { Error = "A positive version is required." }, Json(c));
            return;
        }
        try
        {
            var deleted = await c.RequestServices.GetRequiredService<ISchedulePolicyManager>()
                .DeleteAsync(id, version, c.RequestAborted).ConfigureAwait(false);
            if (deleted)
            {
                c.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            c.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteJson(c, new ErrorResponseBody
            {
                Error = "The schedule policy no longer exists or changed. Reload before deleting it."
            }, Json(c));
        }
        catch (TickerValidatorException exception)
        {
            c.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteJson(c, new ErrorResponseBody { Error = exception.Message }, Json(c));
        }
    }

    private static SchedulePolicy ToSchedulePolicy(SaveSchedulePolicyRequest body) => new()
    {
        Name = body.Name,
        Description = body.Description,
        TimeZoneId = body.TimeZoneId,
        AllowedWeekdays = body.AllowedWeekdays,
        AllowedTimeWindows = body.AllowedTimeWindows ?? new(),
        ExcludedDates = body.ExcludedDates ?? new(),
        ExcludedAnnualDates = body.ExcludedAnnualDates ?? new(),
        DateProviderNames = body.DateProviderNames ?? new(),
        RequiredPolicyNames = body.RequiredPolicyNames ?? new(),
        ExcludedWhenPolicyAllowsNames = body.ExcludedWhenPolicyAllowsNames ?? new(),
    };

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
        var current = await authService.AuthenticateAsync(context);
        context.Response.Headers.CacheControl = "no-store";

        var schemeNames = config.Schemes
            .Select(s => s.LegacyMode.ToString().ToLowerInvariant())
            .ToArray();
        var loginAvailable = config.Schemes
            .Any(s => s.LegacyMode is AuthMode.Jwt or AuthMode.Cookie);

        await WriteJson(context, new AuthInfoResponse
        {
            Authenticated = current.IsAuthenticated,
            Username = current.IsAuthenticated ? current.Username : null,
            UserId = current.IsAuthenticated ? current.UserId : null,
            Mode = info.Mode.ToString().ToLower(),
            Enabled = info.IsEnabled,
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
        var retention = context.RequestServices.GetRequiredService<JobRetentionOptions>();
        var logs = context.RequestServices.GetService<ExecutionLogOptions>() ?? new();
        await WriteJson(context, new DashboardOptionsResponse
        {
            MaxConcurrency = schedulerOptions.MaxConcurrency,
            CurrentMachine = schedulerOptions.NodeIdentifier,
            LastHostExceptionMessage = executionContext.LastHostExceptionMessage,
            SchedulerTimeZone = ToIanaTimeZoneId(schedulerOptions.SchedulerTimeZone),
            MinPollingInterval = schedulerOptions.MinPollingInterval,
            FallbackIntervalChecker = schedulerOptions.FallbackIntervalChecker,
            DefaultExecutionTimeout = schedulerOptions.DefaultExecutionTimeout,
            StaleJobRecoveryEnabled = schedulerOptions.StaleJobRecoveryEnabled,
            DetailedLoggingEnabled = schedulerOptions.DetailedLoggingEnabled,
            ExecutionLogs = new ExecutionLogConfigResponse
            {
                Enabled = logs.Enabled, PersistToDatabase = logs.PersistToDatabase, MinimumLevel = logs.MinimumLevel.ToString(),
                MaxLinesPerExecution = logs.MaxLinesPerExecution, MaxBytesPerExecution = logs.MaxBytesPerExecution,
                MaxStoredExecutions = logs.MaxStoredExecutions, Retention = logs.Retention,
                MemoryRetention = logs.MemoryRetention, FlushInterval = logs.FlushInterval
            },
            Retention = new RetentionConfigResponse
            {
                Enabled = retention.IsEnabled,
                DeleteSucceededAfter = retention.DeleteSucceededAfter,
                DeleteFailedAfter = retention.DeleteFailedAfter,
                DeleteCancelledAfter = retention.DeleteCancelledAfter,
                DeleteSkippedAfter = retention.DeleteSkippedAfter,
                SweepInterval = retention.SweepInterval,
                BatchSize = retention.BatchSize,
                MaxBatchesPerSweep = retention.MaxBatchesPerSweep,
                MaxNodesPerChain = retention.MaxNodesPerChain,
            },
        }, Json(context));
    }

    // ===== License =====

    // Projects the shared, validated license state into a minimal, safe DTO. The raw certificate,
    // envelope, payload, signature, public key, file path, and any internal exception are never exposed.
    private static async Task GetLicense(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<TickerQLicenseStateProvider>().Current;
        await WriteJson(context, new LicenseResponse
        {
            Status = state.Status.ToString(),
            ExecutionAllowed = state.ExecutionAllowed,
            Message = state.Message,
            ActionLabel = state.ActionLabel,
            ActionUrl = state.ActionUrl,
            WorkspaceId = state.WorkspaceId,
            WorkspaceName = state.WorkspaceName,
            Plan = state.Plan,
            Kind = state.Kind,
            IsEvaluation = state.IsEvaluation,
            LicenseId = state.LicenseId,
            IssuedAt = state.IssuedAt,
            ExpiresAt = state.ExpiresAt,
            DaysRemaining = state.DaysRemaining,
            SchemaVersion = state.SchemaVersion,
            Algorithm = state.Algorithm,
            KeyId = state.KeyId,
            AnchoredMinorLine = state.AnchoredMinorLine,
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

    private static async Task GetTimeTickerChain<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        c.Response.Headers.CacheControl = "no-store";
        try
        {
            var chain = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickerChainAsync(id, c.RequestAborted);
            if (chain.Count == 0) { c.Response.StatusCode = 404; return; }
            await WriteJson(c, chain, Json(c));
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "rootId")
        {
            c.Response.StatusCode = 400;
            await WriteJson(c, new ErrorResponseBody { Error = "Chain exceeds the safe traversal limits (500 nodes / 20 child levels)." }, Json(c));
        }
    }

    private static async Task GetTimeTickerChildren<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var children = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickerChildrenAsync(id, c.RequestAborted);
        await WriteJson(c, children, Json(c));
    }

    private static async Task GetCronTickerRequest<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        c.Response.Headers.CacheControl = "no-store";
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var cron = await persistence.GetCronTickerById(id, c.RequestAborted);
        if (cron == null) { c.Response.StatusCode = 404; return; }
        await WriteJson(c, new TickerRequestPayloadResponse { Payload = cron.Request }, Json(c));
    }

    private static async Task GetTimeTickerRequest<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        c.Response.Headers.CacheControl = "no-store";
        var bytes = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickerRequestAsync(id, c.RequestAborted);
        await WriteJson(c, new TickerRequestPayloadResponse { Payload = bytes }, Json(c));
    }

    private static async Task GetTimeTickerResult<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        c.Response.Headers.CacheControl = "no-store";
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var result = await persistence.GetTimeTickerResultAsync(id, c.RequestAborted);
        await WriteJson(c, new TickerResultPayloadResponse
        {
            Payload = result?.ToPayloadArray(),
            Version = result?.Version,
            MediaType = result?.MediaType,
            ContractId = result?.ContractId,
            ContractType = result?.ContractType,
        }, Json(c));
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

    // Bounded diagnostic tail, combining the local unflushed buffer with optional shared storage.
    private static async Task GetTickerLogTail(HttpContext c, Guid tickerId)
    {
        var body = new TickerLogTailResponseBody();
        c.Response.Headers.CacheControl = "no-store";
        var options = c.RequestServices.GetService<ExecutionLogOptions>() ?? new();
        body.Storage = !options.Enabled ? "disabled" : options.PersistToDatabase ? "database" : "memory";
        if (c.RequestServices.GetService<ITickerExecutionLogStore>()?.IsEphemeral(tickerId) == true)
            body.Storage = "memory";
        var result = c.RequestServices.GetService<ExecutionLogReader>() is { } reader
            ? await reader.ReadAsync(tickerId, c.RequestAborted)
            : new ExecutionLogReadResult(c.RequestServices.GetService<ITickerExecutionLogStore>()?.GetTail(tickerId)
                ?? Array.Empty<TickerExecutionLogLine>(), false, !options.PersistToDatabase);
        body.Truncated = result.Truncated;
        body.PersistenceAvailable = result.PersistenceAvailable;
        body.WriteHealth = GetLogWriteHealth(c);
        foreach (var line in result.Lines)
        {
            body.Lines.Add(new TickerLogLineResponse
            {
                Id = line.Id,
                UnixMs = line.UnixMs,
                Level = line.Level,
                Source = line.LifecycleEvent == TickerLifecycleEvent.None ? string.Empty : "scheduler",
                LifecycleEvent = line.LifecycleEvent == TickerLifecycleEvent.None ? null : line.LifecycleEvent.ToString(),
                Status = line.Status?.ToString(),
                Attempt = line.Attempt,
                Message = line.Message,
                Category = line.Category,
                FunctionName = line.FunctionName,
            });
        }
        await WriteJson(c, body, Json(c));
    }

    private static ExecutionLogHealthResponse GetLogWriteHealth(HttpContext c)
    {
        var health = c.RequestServices.GetService<TickerQRuntimeDiagnostics>();
        var buffer = c.RequestServices.GetService<InMemoryTickerExecutionLogStore>()?.Health;
        return new ExecutionLogHealthResponse
        {
            WritesAvailable = health?.LogWritesAvailable,
            LastSuccessfulFlushUtc = health?.LastSuccessfulLogFlushUtc,
            LastFailureUtc = health?.LastLogWriteFailureUtc,
            WriteFailures = health?.LogWriteFailures ?? 0,
            PendingWrites = buffer?.PendingWrites ?? 0,
            DroppedUnflushedExecutions = buffer?.DroppedUnflushedExecutions ?? 0
        };
    }

    private static async Task GetRuntimeDiagnostics(HttpContext c)
    {
        c.Response.Headers.CacheControl = "no-store";
        var health = c.RequestServices.GetService<TickerQRuntimeDiagnostics>();
        await WriteJson(c, new RuntimeDiagnosticsResponse
        {
            Node = c.RequestServices.GetRequiredService<SchedulerOptionsBuilder>().NodeIdentifier,
            LastSuccessfulFallbackScanUtc = health?.LastSuccessfulFallbackScanUtc,
            LastFallbackFailureUtc = health?.LastFallbackFailureUtc,
            FallbackScanFailures = health?.FallbackScanFailures ?? 0,
            ExecutionLogs = GetLogWriteHealth(c)
        }, Json(c));
    }

    private static async Task GetTickerLogBatch(HttpContext c)
    {
        var request = await ReadJsonAsync<TickerLogBatchRequest>(c, Json(c));
        if (request?.Items == null || request.Items.Count is < 1 or > ExecutionLogReader.MaximumBatchSize
            || request.Items.Any(x => x == null || x.TickerId == Guid.Empty || x.Cursor?.Length > 64)
            || request.Items.Select(x => x.TickerId).Distinct().Count() != request.Items.Count)
        {
            c.Response.StatusCode = 400;
            await WriteJson(c, new ErrorResponseBody { Error = "Provide 1–50 unique ticker IDs with valid log cursors." }, Json(c));
            return;
        }
        c.Response.Headers.CacheControl = "no-store";
        var options = c.RequestServices.GetRequiredService<ExecutionLogOptions>();
        var reader = c.RequestServices.GetRequiredService<ExecutionLogReader>();
        var results = await reader.ReadBatchAsync(request.Items.Select(x => x.TickerId).ToArray(), c.RequestAborted);
        var response = new TickerLogBatchResponse();
        var health = GetLogWriteHealth(c);
        foreach (var item in request.Items)
        {
            var delta = LogTailDelta.Create(item.TickerId, results[item.TickerId], item.Cursor);
            delta.Tail.Storage = !options.Enabled ? "disabled" : options.PersistToDatabase ? "database" : "memory";
            delta.Tail.WriteHealth = health;
            response.Items.Add(delta);
        }
        await WriteJson(c, response, Json(c));
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

    private static async Task CancelPendingTimeTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var result = await c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>()
            .CancelAsync(id, c.RequestAborted);
        if (!result.IsSucceeded)
        {
            c.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TickerQ.Dashboard")
                .LogWarning(result.Exception, "Pending ticker cancellation failed for {TickerId}", id);
            c.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteJson(c, new ErrorResponseBody { Error = "The job could not be cancelled. Refresh its status and try again." }, Json(c));
            return;
        }
        if (result.AffectedRows > 0)
        {
            await WriteJson(c, new CancellationResponseBody { Outcome = "Cancelled", Affected = result.AffectedRows }, Json(c));
            return;
        }

        var ticker = await DataService<TTimeTicker, TCronTicker>(c).GetTimeTickerByIdAsync(id, c.RequestAborted);
        if (ticker?.Status == TickerStatus.Cancelled)
        {
            await WriteJson(c, new CancellationResponseBody { Outcome = "Cancelled" }, Json(c));
            return;
        }
        c.Response.StatusCode = ticker == null ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict;
        await WriteJson(c, new ErrorResponseBody
        {
            Error = ticker == null ? "This job no longer exists." :
                "This job is no longer pending. Refresh its status before requesting cancellation of a running job."
        }, Json(c));
    }

    private static Task RequestTimeTickerCancellation<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        => RequestRunningCancellation<TTimeTicker, TCronTicker>(c, id, TickerType.TimeTicker);

    private static Task RequestCronOccurrenceCancellation<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        => RequestRunningCancellation<TTimeTicker, TCronTicker>(c, id, TickerType.CronTickerOccurrence);

    private static async Task RequestRunningCancellation<TTimeTicker, TCronTicker>(HttpContext c, Guid id, TickerType type)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var requested = await DataService<TTimeTicker, TCronTicker>(c)
            .RequestRunningTickerCancellationAsync(id, type, c.RequestAborted);
        c.Response.StatusCode = requested ? StatusCodes.Status202Accepted : StatusCodes.Status409Conflict;
        if (requested)
            await WriteJson(c, new CancellationResponseBody { Outcome = "Requested", Affected = 1 }, Json(c));
        else
            await WriteJson(c, new ErrorResponseBody
            {
                Error = "No matching running execution could be signalled. It may have completed, changed owner, or the operational provider may not support cluster control."
            }, Json(c));
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
            SchedulePolicyName = body.SchedulePolicyName,
            BlockedOccurrenceBehavior = body.BlockedOccurrenceBehavior,
            MissedExecutionAction = body.MissedExecutionAction ?? MissedExecutionAction.RunOnce,
            MaximumLateness = ToMaximumLateness(body.MaximumLatenessSeconds),
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
        if (!result.IsSucceeded) { await WriteTickerError(c, result.Exception); return; }
        if (result.AffectedRows == 0)
        {
            c.Response.StatusCode = 409;
            await WriteJson(c, new ErrorResponseBody { Error = "No job was deleted. The chain may be protected, active, missing, or changed. Refresh and try again." }, Json(c));
            return;
        }
        c.Response.StatusCode = 204;
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
        if (!result.IsSucceeded) { await WriteTickerError(c, result.Exception); return; }
        if (result.AffectedRows == 0)
        {
            c.Response.StatusCode = 409;
            await WriteJson(c, new ErrorResponseBody { Error = "No jobs were deleted. Selected chains may be protected, active, missing, or changed. Refresh and try again." }, Json(c));
            return;
        }
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

        // Each selected occurrence is revived in place. Deduplicate occurrence IDs without
        // collapsing separate historical occurrences of the same cron definition.
        var occurrenceIdsTriggered = new HashSet<Guid>();
        var cronManager = c.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        foreach (var item in body.Items)
        {
            if (string.Equals(item.Type, "TimeTicker", StringComparison.OrdinalIgnoreCase))
            {
                if (await repository.RunTimeTickerOnDemandAsync(item.Id, c.RequestAborted))
                    affected++;
            }
            else if (item.CronTickerId.HasValue && occurrenceIdsTriggered.Add(item.Id))
            {
                var result = await cronManager.RerunOccurrenceAsync(item.Id, c.RequestAborted);
                if (result.IsSucceeded)
                    affected++;
            }
        }

        await WriteJson(c, new BulkActionResponseBody { Affected = affected }, Json(c));
    }

    private static async Task TestRunFunction(HttpContext c)
    {
        var body = await ReadJsonAsync<TestRunRequest>(c, Json(c));
        if (body == null || string.IsNullOrWhiteSpace(body.Function))
        {
            c.Response.StatusCode = 400;
            await WriteJson(c, new ErrorResponseBody { Error = "Select a function to test." }, Json(c));
            return;
        }

        if (TickerChainInfrastructure.IsParallelGroup(body.Function))
        {
            c.Response.StatusCode = 400;
            await WriteJson(c, new ErrorResponseBody { Error = "A structural group is not an executable function." }, Json(c));
            return;
        }

        if (!TickerFunctionProvider.TickerFunctions.TryGetValue(body.Function, out var registered) ||
            !TickerFunctionProvider.TickerFunctionDescriptors.TryGetValue(body.Function, out var descriptor))
        {
            c.Response.StatusCode = 404;
            await WriteJson(c, new ErrorResponseBody { Error = $"TickerFunction '{body.Function}' is not registered on this node." }, Json(c));
            return;
        }

        if (TickerFunctionProvider.ResolveNodeName(body.Function) != null)
        {
            c.Response.StatusCode = 409;
            await WriteJson(c, new ErrorResponseBody
            {
                Error = "Run test is available only for functions registered on this dashboard host. Remote functions require a durable execution."
            }, Json(c));
            return;
        }

        var validation = TickerRequestPayloadValidator.Validate(body.Function, body.Request);
        if (!validation.IsValid)
        {
            c.Response.StatusCode = 400;
            await WriteJson(c, new ErrorResponseBody
            {
                Error = string.Join("; ", validation.Errors.Select(error => error.ToString()))
            }, Json(c));
            return;
        }

        var timeoutSeconds = Math.Clamp(body.TimeoutSeconds ?? 30, 1, 300);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var executionContext = c.RequestServices.GetRequiredService<TickerExecutionContext>();
        var handler = c.RequestServices.GetRequiredService<ITickerExecutionTaskHandler>();
        var concurrencyGate = c.RequestServices.GetRequiredService<ITickerFunctionConcurrencyGate>();
        var lifecycle = c.RequestServices.GetService<TickerLifecycleLogger>();
        var context = new InternalFunctionContext
        {
            TickerId = id,
            ChainRootId = id,
            RuntimePartitionKey = executionContext.RuntimePartition.StorageKey,
            FunctionName = body.Function,
            Type = TickerType.TimeTicker,
            Status = TickerStatus.InProgress,
            ExecutionTime = now,
            TimeoutSeconds = timeoutSeconds,
            CachedDelegate = registered.Delegate,
            CachedPriority = registered.Priority,
            CachedMaxConcurrency = registered.MaxConcurrency,
            RequestContractVersion = descriptor.ContractVersion,
            RequestContractFingerprint = descriptor.Request?.Fingerprint,
            RequestPayload = body.Request,
            IsTestRun = true,
        };
        lifecycle?.Created(id, body.Function, TickerType.TimeTicker, now,
            executionContext.RuntimePartition.StorageKey, isEphemeral: true);
        lifecycle?.Queued(context);

        var stopwatch = Stopwatch.StartNew();
        IDisposable? permit;
        try
        {
            permit = registered.MaxConcurrency > 0
                ? await concurrencyGate.AcquireAsync(body.Function, registered.MaxConcurrency, timeout.Token)
                : null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !c.RequestAborted.IsCancellationRequested)
        {
            stopwatch.Stop();
            await WriteJson(c, new TestRunResponse
            {
                ExecutionId = id,
                Status = TickerStatus.Cancelled,
                Success = false,
                Error = $"The test run could not acquire function capacity within {timeoutSeconds} seconds.",
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            }, Json(c));
            return;
        }

        using (permit)
        {
            var result = await handler.ExecuteWorkerTaskAsync(context, isDue: false, timeout.Token);
            lifecycle?.Committed(context);
            stopwatch.Stop();
            await WriteJson(c, new TestRunResponse
            {
                ExecutionId = id,
                Status = result.Status,
                Success = result.Success,
                Error = result.Error,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                Result = result.ResultEnvelope?.ToPayloadArray(),
                ResultMediaType = result.ResultEnvelope?.MediaType,
                ResultContractId = result.ResultEnvelope?.ContractId,
            }, Json(c));
        }
    }

    private static async Task TestRunChain(HttpContext c)
    {
        var body = await ReadJsonAsync<AddTimeTickerChainRequest>(c, Json(c));
        if (body?.Root == null)
        {
            c.Response.StatusCode = 400;
            await WriteJson(c, new ErrorResponseBody { Error = "A chain root is required." }, Json(c));
            return;
        }

        var validationError = ValidateTestChain(body.Root);
        if (validationError != null)
        {
            c.Response.StatusCode = 400;
            await WriteJson(c, new ErrorResponseBody { Error = validationError }, Json(c));
            return;
        }

        var executionContext = c.RequestServices.GetRequiredService<TickerExecutionContext>();
        var metadata = new Dictionary<Guid, DashboardTestRunStepMetadata>();
        var root = BuildTestChainContext(body.Root, null, null,
            executionContext.RuntimePartition.StorageKey, metadata);
        TickerExecutionContext.CacheFunctionReferences([root]);

        var registry = c.RequestServices.GetRequiredService<DashboardTestRunRegistry>();
        if (!registry.TryCreate(root, metadata, out var session))
        {
            c.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await WriteJson(c, new ErrorResponseBody
            {
                Error = "Too many chain tests are currently running. Wait for one to finish and try again."
            }, Json(c));
            return;
        }

        registry.Start(session, c.RequestServices.GetRequiredService<ITickerExecutionTaskHandler>());
        c.Response.StatusCode = StatusCodes.Status202Accepted;
        await WriteJson(c, CreateTestRunChainResponse(session), Json(c));
    }

    private static async Task GetTestRunChain(HttpContext c, Guid id)
    {
        if (!c.RequestServices.GetRequiredService<DashboardTestRunRegistry>()
                .TryGet(id, out var session))
        {
            c.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteJson(c, new ErrorResponseBody
            {
                Error = "This temporary chain test is no longer available."
            }, Json(c));
            return;
        }

        c.Response.Headers.CacheControl = "no-store";
        await WriteJson(c, CreateTestRunChainResponse(session), Json(c));
    }

    private static TestRunChainResponse CreateTestRunChainResponse(DashboardTestRunSession session)
    {
        var steps = new List<TestRunChainStepResponse>();
        var pending = new Stack<InternalFunctionContext>();
        pending.Push(session.Root);
        while (pending.TryPop(out var step))
        {
            session.Metadata.TryGetValue(step.TickerId, out var metadata);
            steps.Add(new TestRunChainStepResponse
            {
                ExecutionId = step.TickerId,
                ParentExecutionId = step.ParentId,
                Function = TickerChainInfrastructure.IsParallelGroup(step.FunctionName)
                    ? "Group"
                    : step.FunctionName,
                Description = metadata?.Description,
                Status = step.Status,
                RunCondition = step.ParentId.HasValue ? step.RunCondition : null,
                Priority = step.CachedPriority,
                Retries = step.Retries,
                RetryCount = step.RetryCount,
                CreatedAtUtc = session.StartedAtUtc,
                ExecutedAtUtc = step.ExecutedAt == default ? null : step.ExecutedAt,
                ProgressUpdatedAtUtc = step.ProgressUpdatedAt,
                ChildCount = step.TimeTickerChildren?.Count ?? 0,
                Error = step.Status is TickerStatus.Done or TickerStatus.DueDone
                    ? null
                    : step.ExceptionDetails,
                ElapsedMilliseconds = step.ElapsedTime,
                ProgressPercent = step.ProgressPercent,
                ProgressMessage = step.ProgressMessage,
                Result = step.ResultEnvelope?.ToPayloadArray(),
                ResultMediaType = step.ResultEnvelope?.MediaType,
                ResultContractId = step.ResultEnvelope?.ContractId,
                IsGroup = TickerChainInfrastructure.IsParallelGroup(step.FunctionName),
            });
            if (step.TimeTickerChildren == null) continue;
            for (var i = step.TimeTickerChildren.Count - 1; i >= 0; i--)
                pending.Push(step.TimeTickerChildren[i]);
        }

        return new TestRunChainResponse
        {
            RootExecutionId = session.Id,
            Completed = session.Completed,
            StartedAtUtc = session.StartedAtUtc,
            FinishedAtUtc = session.FinishedAtUtc,
            Error = session.Error,
            Success = session.Completed && session.Error == null &&
                      steps.All(step => step.Status is not (TickerStatus.Failed or TickerStatus.Cancelled)),
            Steps = steps,
        };
    }

    private static string? ValidateTestChain(TimeTickerNodeRequest root)
    {
        var pending = new Stack<(TimeTickerNodeRequest Node, int Depth)>();
        pending.Push((root, 1));
        var count = 0;
        while (pending.TryPop(out var item))
        {
            if (++count > 500) return "A test chain can contain at most 500 nodes.";
            if (item.Depth > 20) return "A test chain can contain at most 20 child levels.";
            var node = item.Node;
            if (string.IsNullOrWhiteSpace(node.Function))
                return "Every active chain node must select a function.";

            if (!TickerChainInfrastructure.IsParallelGroup(node.Function))
            {
                if (TickerFunctionProvider.ResolveNodeName(node.Function) != null)
                    return $"Test chain is available only for functions registered on this dashboard host. '{node.Function}' is remote.";
                if (!TickerFunctionProvider.TickerFunctions.ContainsKey(node.Function) ||
                    !TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey(node.Function))
                    return $"TickerFunction '{node.Function}' is not registered on this node.";
                var payload = TickerRequestPayloadValidator.Validate(node.Function, node.Request);
                if (!payload.IsValid)
                    return $"{node.Function}: {string.Join("; ", payload.Errors.Select(error => error.ToString()))}";
            }

            if (node.Retries is < 0)
                return $"Retries for '{node.Function}' cannot be negative.";
            if (node.TimeoutSeconds is < 0)
                return $"Timeout for '{node.Function}' cannot be negative.";
            if (node.Children == null) continue;
            for (var i = node.Children.Count - 1; i >= 0; i--)
                pending.Push((node.Children[i], item.Depth + 1));
        }
        return null;
    }

    private static InternalFunctionContext BuildTestChainContext(
        TimeTickerNodeRequest node, Guid? parentId, Guid? rootId, string partition,
        IDictionary<Guid, DashboardTestRunStepMetadata> metadata)
    {
        var id = Guid.NewGuid();
        var isGroup = TickerChainInfrastructure.IsParallelGroup(node.Function);
        var descriptor = isGroup
            ? null
            : TickerFunctionProvider.TickerFunctionDescriptors[node.Function];
        var context = new InternalFunctionContext
        {
            TickerId = id,
            ParentId = parentId,
            ChainRootId = rootId ?? id,
            ChainGeneration = rootId == null ? Guid.NewGuid() : null,
            RuntimePartitionKey = partition,
            FunctionName = node.Function,
            Type = TickerType.TimeTicker,
            Status = TickerStatus.Idle,
            ExecutionTime = DateTime.UtcNow,
            Retries = Math.Max(0, node.Retries ?? 0),
            RetryIntervals = node.RetryIntervalsSeconds?.ToArray(),
            RunCondition = node.RunCondition ?? RunCondition.OnSuccess,
            TimeoutSeconds = node.TimeoutSeconds is > 0 ? node.TimeoutSeconds : null,
            RequestPayload = node.Request,
            RequestContractVersion = descriptor?.ContractVersion,
            RequestContractFingerprint = descriptor?.Request?.Fingerprint,
            IsTestRun = true,
        };
        metadata[id] = new DashboardTestRunStepMetadata(node.Description);
        if (node.Children != null)
        {
            foreach (var child in node.Children)
                context.TimeTickerChildren.Add(BuildTestChainContext(
                    child, id, rootId ?? id, partition, metadata));
        }
        return context;
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

    private static async Task RetryTimeTickerFromHere<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var result = await manager.RetryFromAsync(id, c.RequestAborted);
        if (!result.IsSucceeded || result.Result == null)
        {
            await WriteTickerError(c, result.Exception);
            return;
        }
        await WriteJson(c, new AddTickerResponseBody { Id = result.Result.Id.ToString() }, Json(c));
    }

    private static async Task SetChainDeleteBehavior<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<SetChainDeleteBehaviorRequest>(c, Json(c));
        if (body?.DeleteBehavior is not { } behavior) { c.Response.StatusCode = 400; return; }
        var manager = c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
        var result = await manager.SetDeleteBehaviorAsync(id, behavior, c.RequestAborted);
        if (!result.IsSucceeded) { await WriteTickerError(c, result.Exception); return; }
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
        if (body.ReplaceSchedulePolicy || body.SchedulePolicyName != null)
            entity.SchedulePolicyName = string.IsNullOrWhiteSpace(body.SchedulePolicyName) ? null : body.SchedulePolicyName;
        if (body.BlockedOccurrenceBehavior.HasValue)
            entity.BlockedOccurrenceBehavior = body.BlockedOccurrenceBehavior;
        if (body.MissedExecutionAction.HasValue)
        {
            entity.MissedExecutionAction = body.MissedExecutionAction.Value;
            if (body.MissedExecutionAction == MissedExecutionAction.RunOnce && !body.MaximumLatenessSeconds.HasValue)
                entity.MaximumLateness = null;
        }
        if (body.MaximumLatenessSeconds.HasValue)
            entity.MaximumLateness = ToMaximumLateness(body.MaximumLatenessSeconds);

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
        rootEntity.SchedulePolicyName = body.SchedulePolicyName;
        rootEntity.BlockedOccurrenceBehavior = body.BlockedOccurrenceBehavior;
        rootEntity.MissedExecutionAction = body.MissedExecutionAction ?? MissedExecutionAction.RunOnce;
        rootEntity.MaximumLateness = ToMaximumLateness(body.MaximumLatenessSeconds);

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
        newRoot.SchedulePolicyName = body.SchedulePolicyName;
        newRoot.BlockedOccurrenceBehavior = body.BlockedOccurrenceBehavior;
        newRoot.MissedExecutionAction = body.MissedExecutionAction ?? MissedExecutionAction.RunOnce;
        newRoot.MaximumLateness = ToMaximumLateness(body.MaximumLatenessSeconds);

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
            Chain = body.Chain,
            Description = body.Description,
            Retries = body.Retries ?? 0,
            Request = body.Request,
            RetryIntervals = body.RetryIntervalsSeconds,
            SkipIfAlreadyRunning = body.SkipIfAlreadyRunning ?? false,
            IsEnabled = body.IsEnabled ?? true,
            OnStale = body.OnStale ?? StaleAction.Restart,
            TimeoutSeconds = body.TimeoutSeconds is > 0 ? body.TimeoutSeconds : null,
            TimeZoneId = body.TimeZoneId,
            SchedulePolicyName = body.SchedulePolicyName,
            BlockedOccurrenceBehavior = string.IsNullOrWhiteSpace(body.SchedulePolicyName)
                ? null
                : BlockedOccurrenceBehavior.Skip,
            MissedExecutionAction = body.MissedExecutionAction ?? MissedExecutionAction.RunOnce,
            MaximumLateness = ToMaximumLateness(body.MaximumLatenessSeconds),
            CatchUpMode = body.CatchUpMode ?? CronCatchUpMode.Skip,
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
        if (body.ReplaceChain || body.Chain != null)        entity.Chain          = body.Chain;
        if (body.Description != null)                      entity.Description    = body.Description;
        if (body.Retries.HasValue)                         entity.Retries        = body.Retries.Value;
        if (body.RetryIntervalsSeconds != null)            entity.RetryIntervals = body.RetryIntervalsSeconds.Length > 0 ? body.RetryIntervalsSeconds : null;
        if (body.Request != null)                          entity.Request        = body.Request.Length > 0 ? body.Request : null;
        if (body.SkipIfAlreadyRunning.HasValue)             entity.SkipIfAlreadyRunning = body.SkipIfAlreadyRunning.Value;
        if (body.IsEnabled.HasValue)                       entity.IsEnabled      = body.IsEnabled.Value;
        if (body.OnStale.HasValue)                         entity.OnStale        = body.OnStale.Value;
        if (body.TimeoutSeconds.HasValue)                  entity.TimeoutSeconds = body.TimeoutSeconds.Value > 0 ? body.TimeoutSeconds.Value : null;
        if (body.TimeZoneId != null)                       entity.TimeZoneId = string.IsNullOrWhiteSpace(body.TimeZoneId) ? null : body.TimeZoneId;
        if (body.ReplaceSchedulePolicy || body.SchedulePolicyName != null)
            entity.SchedulePolicyName = string.IsNullOrWhiteSpace(body.SchedulePolicyName) ? null : body.SchedulePolicyName;
        entity.BlockedOccurrenceBehavior = string.IsNullOrWhiteSpace(entity.SchedulePolicyName)
            ? null
            : BlockedOccurrenceBehavior.Skip;
        if (body.MissedExecutionAction.HasValue)
        {
            entity.MissedExecutionAction = body.MissedExecutionAction.Value;
            if (body.MissedExecutionAction == MissedExecutionAction.RunOnce && !body.MaximumLatenessSeconds.HasValue)
                entity.MaximumLateness = null;
        }
        if (body.MaximumLatenessSeconds.HasValue)
            entity.MaximumLateness = ToMaximumLateness(body.MaximumLatenessSeconds);
        if (body.CatchUpMode.HasValue)
            entity.CatchUpMode = body.CatchUpMode.Value;

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
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        if (await persistence.GetCronTickerById(id, c.RequestAborted) == null) { c.Response.StatusCode = 404; return; }
        var manager = c.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var result = await manager.SetEnabledAsync(id, body.IsEnabled, c.RequestAborted);
        if (!result.IsSucceeded) { await WriteTickerError(c, result.Exception); return; }
        c.Response.StatusCode = 204;
    }

    private static async Task RunCronTickerOnDemand<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        await DataService<TTimeTicker, TCronTicker>(c).RunCronTickerOnDemandAsync(id, c.RequestAborted);
        c.Response.StatusCode = 204;
    }

    private static async Task RerunCronOccurrence<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var manager = c.RequestServices.GetRequiredService<ICronTickerManager<TCronTicker>>();
        var result = await manager.RerunOccurrenceAsync(id, c.RequestAborted);
        if (!result.IsSucceeded)
        {
            await WriteTickerError(c, result.Exception);
            return;
        }
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

    // ===== Saved templates and time-ticker drafts =====

    private static async Task ListDefinitions<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        SavedTickerDefinitionKind? kind = Enum.TryParse<SavedTickerDefinitionKind>(
            c.Request.Query["kind"], true, out var parsedKind) ? parsedKind : null;
        SavedTickerDefinitionTarget? target = Enum.TryParse<SavedTickerDefinitionTarget>(
            c.Request.Query["target"], true, out var parsedTarget) ? parsedTarget : null;
        var rows = await store.ListDefinitionsAsync(kind, target, c.RequestAborted).ConfigureAwait(false);
        await WriteJson(c, rows.Select(row => ToDefinitionResponse(row, c)).ToList(), Json(c));
    }

    private static async Task GetDefinition<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        var row = await store.GetDefinitionAsync(id, c.RequestAborted).ConfigureAwait(false);
        if (row == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        await WriteJson(c, ToDefinitionResponse(row, c), Json(c));
    }

    private static async Task CreateDefinition<TTimeTicker, TCronTicker>(HttpContext c)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<SaveTickerDefinitionRequest>(c, Json(c));
        string? error = null;
        if (body == null || !ValidateDefinitionRequest(body, out error))
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody { Error = error ?? "A saved definition is required." }, Json(c));
            return;
        }
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        if (!await EnsureTemplateNameAvailableAsync(
                c, store, body.Kind, body.Target, body.Name, null).ConfigureAwait(false)) return;
        var payload = CopyPayload(body.Payload!, body.IncludeRequestData);
        var row = new SavedTickerDefinition
        {
            Kind = body.Kind,
            Target = body.Target,
            Name = body.Name.Trim(),
            Description = body.Description?.Trim(),
            IncludesRequestData = body.IncludeRequestData,
            SchemaVersion = 1,
            DefinitionJson = JsonSerializer.Serialize(
                payload,
                DashboardJsonSerializerContext.Default.SavedDefinitionPayload),
        };
        SavedTickerDefinition created;
        try { created = await store.CreateDefinitionAsync(row, c.RequestAborted).ConfigureAwait(false); }
        catch (DuplicateSavedTickerDefinitionNameException ex)
        {
            await WriteDefinitionNameConflictAsync(c, ex.Message).ConfigureAwait(false);
            return;
        }
        c.Response.StatusCode = StatusCodes.Status201Created;
        await WriteJson(c, ToDefinitionResponse(created, c), Json(c));
    }

    private static async Task UpdateDefinition<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<UpdateTickerDefinitionRequest>(c, Json(c));
        string? error = null;
        if (body == null || body.Version <= 0 || !ValidateDefinitionRequest(body, out error))
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody { Error = error ?? "A versioned saved definition is required." }, Json(c));
            return;
        }
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        if (!await EnsureTemplateNameAvailableAsync(
                c, store, body.Kind, body.Target, body.Name, id).ConfigureAwait(false)) return;
        var row = new SavedTickerDefinition
        {
            Id = id, Kind = body.Kind, Target = body.Target, Name = body.Name.Trim(),
            Description = body.Description?.Trim(), IncludesRequestData = body.IncludeRequestData,
            SchemaVersion = 1,
            DefinitionJson = JsonSerializer.Serialize(
                CopyPayload(body.Payload!, body.IncludeRequestData),
                DashboardJsonSerializerContext.Default.SavedDefinitionPayload),
        };
        SavedTickerDefinition updated;
        try { updated = await store.UpdateDefinitionAsync(row, body.Version, c.RequestAborted).ConfigureAwait(false); }
        catch (DuplicateSavedTickerDefinitionNameException ex)
        {
            await WriteDefinitionNameConflictAsync(c, ex.Message).ConfigureAwait(false);
            return;
        }
        if (updated == null)
        {
            c.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteJson(c, new ErrorResponseBody { Error = "This definition changed in another session. Refresh it before saving again." }, Json(c));
            return;
        }
        await WriteJson(c, ToDefinitionResponse(updated, c), Json(c));
    }

    private static async Task DeleteDefinition<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (!long.TryParse(c.Request.Query["version"], out var version) || version <= 0)
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody { Error = "The current definition version is required." }, Json(c));
            return;
        }
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        c.Response.StatusCode = await store.DeleteDefinitionAsync(id, version, c.RequestAborted).ConfigureAwait(false)
            ? StatusCodes.Status204NoContent : StatusCodes.Status409Conflict;
    }

    private static async Task CreateDefinitionFromTimeTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<SaveExistingTickerDefinitionRequest>(c, Json(c));
        if (body == null || string.IsNullOrWhiteSpace(body.Name))
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var selected = await persistence.GetTimeTickerById(id, c.RequestAborted).ConfigureAwait(false);
        if (selected == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var rootId = selected.ChainRootId ?? selected.Id;
        var root = rootId == selected.Id ? selected : await persistence.GetTimeTickerById(rootId, c.RequestAborted).ConfigureAwait(false);
        if (root == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var payload = new SavedDefinitionPayload
        {
            ExecutionTime = root.ExecutionTime,
            SchedulePolicyName = root.SchedulePolicyName,
            BlockedOccurrenceBehavior = root.BlockedOccurrenceBehavior,
            MissedExecutionAction = root.MissedExecutionAction,
            MaximumLatenessSeconds = ToMaximumLatenessSeconds(root.MaximumLateness),
            Root = ToNodeRequest(root, body.IncludeRequestData),
        };
        if (payload.Root.Children is not { Count: > 0 })
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody
            {
                Error = "Templates are available only for chains with at least two jobs.",
            }, Json(c));
            return;
        }
        if (!await EnsureTemplateNameAvailableAsync(c, store, SavedTickerDefinitionKind.Template,
                SavedTickerDefinitionTarget.TimeTicker, body.Name, null).ConfigureAwait(false)) return;
        SavedTickerDefinition created;
        try { created = await store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template, Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = body.Name.Trim(), Description = body.Description?.Trim(),
            IncludesRequestData = body.IncludeRequestData,
            DefinitionJson = JsonSerializer.Serialize(
                payload,
                DashboardJsonSerializerContext.Default.SavedDefinitionPayload),
            SchemaVersion = 1,
        }, c.RequestAborted).ConfigureAwait(false); }
        catch (DuplicateSavedTickerDefinitionNameException ex)
        {
            await WriteDefinitionNameConflictAsync(c, ex.Message).ConfigureAwait(false);
            return;
        }
        c.Response.StatusCode = StatusCodes.Status201Created;
        await WriteJson(c, ToDefinitionResponse(created, c), Json(c));
    }

    private static async Task CreateDefinitionFromCronTicker<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<SaveExistingTickerDefinitionRequest>(c, Json(c));
        if (body == null || string.IsNullOrWhiteSpace(body.Name))
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var cron = await persistence.GetCronTickerById(id, c.RequestAborted).ConfigureAwait(false);
        if (cron == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var root = new TimeTickerNodeRequest
        {
            Function = cron.Function, Description = cron.Description, Retries = cron.Retries,
            RetryIntervalsSeconds = cron.RetryIntervals, Request = body.IncludeRequestData ? cron.Request : null,
            OnStale = cron.OnStale, TimeoutSeconds = cron.TimeoutSeconds,
            DeleteBehavior = cron.Chain?.DeleteBehavior ?? ChainDeleteBehavior.Restrict,
            Children = cron.Chain?.Children.Select(child => ToNodeRequest(child, body.IncludeRequestData)).ToList()
                ?? new List<TimeTickerNodeRequest>(),
        };
        var payload = new SavedDefinitionPayload
        {
            Expression = cron.Expression, SkipIfAlreadyRunning = cron.SkipIfAlreadyRunning,
            TimeZoneId = cron.TimeZoneId, SchedulePolicyName = cron.SchedulePolicyName,
            BlockedOccurrenceBehavior = cron.BlockedOccurrenceBehavior, Root = root,
            MissedExecutionAction = cron.MissedExecutionAction,
            MaximumLatenessSeconds = ToMaximumLatenessSeconds(cron.MaximumLateness),
            CatchUpMode = cron.CatchUpMode,
        };
        if (root.Children is not { Count: > 0 })
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJson(c, new ErrorResponseBody
            {
                Error = "Templates are available only for chains with at least two jobs.",
            }, Json(c));
            return;
        }
        if (!await EnsureTemplateNameAvailableAsync(c, store, SavedTickerDefinitionKind.Template,
                SavedTickerDefinitionTarget.CronTicker, body.Name, null).ConfigureAwait(false)) return;
        SavedTickerDefinition created;
        try { created = await store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template, Target = SavedTickerDefinitionTarget.CronTicker,
            Name = body.Name.Trim(), Description = body.Description?.Trim(),
            IncludesRequestData = body.IncludeRequestData,
            DefinitionJson = JsonSerializer.Serialize(
                payload,
                DashboardJsonSerializerContext.Default.SavedDefinitionPayload),
            SchemaVersion = 1,
        }, c.RequestAborted).ConfigureAwait(false); }
        catch (DuplicateSavedTickerDefinitionNameException ex)
        {
            await WriteDefinitionNameConflictAsync(c, ex.Message).ConfigureAwait(false);
            return;
        }
        c.Response.StatusCode = StatusCodes.Status201Created;
        await WriteJson(c, ToDefinitionResponse(created, c), Json(c));
    }

    private static async Task ActivateDraft<TTimeTicker, TCronTicker>(HttpContext c, Guid id)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var body = await ReadJsonAsync<ActivateTickerDraftRequest>(c, Json(c));
        if (body == null || body.Version <= 0)
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!TryGetDefinitionStore<TTimeTicker, TCronTicker>(c, out var store)) return;
        var persistence = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
        var row = await store.GetDefinitionAsync(id, c.RequestAborted).ConfigureAwait(false);
        if (row == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        if (row.Kind != SavedTickerDefinitionKind.Draft || row.Target != SavedTickerDefinitionTarget.TimeTicker)
        {
            c.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteJson(c, new ErrorResponseBody { Error = "Only a time-ticker draft can be activated." }, Json(c));
            return;
        }
        if (row.ActivatedRootTickerId is { } activatedId)
        {
            await WriteJson(c, new AddTimeTickerChainResponseBody { RootId = activatedId.ToString(), CreatedCount = 0 }, Json(c));
            return;
        }
        if (row.ActivationToken is { } existingToken && row.PendingRootTickerId is { } existingRoot)
        {
            var published = await persistence.GetTimeTickerById(existingRoot, c.RequestAborted).ConfigureAwait(false);
            if (published != null)
            {
                await store.CompleteDraftActivationAsync(id, existingToken, existingRoot, DateTime.UtcNow, c.RequestAborted)
                    .ConfigureAwait(false);
                await WriteJson(c, new AddTimeTickerChainResponseBody
                {
                    RootId = existingRoot.ToString(), CreatedCount = 1 + CountDescendants(published),
                }, Json(c));
                return;
            }
            if (row.ActivationClaimedAtUtc > DateTime.UtcNow.AddMinutes(-5))
            {
                c.Response.StatusCode = StatusCodes.Status409Conflict;
                await WriteJson(c, new ErrorResponseBody { Error = "This draft is already being activated." }, Json(c));
                return;
            }
            await store.ReleaseDraftActivationAsync(id, existingToken, c.RequestAborted).ConfigureAwait(false);
            row = await store.GetDefinitionAsync(id, c.RequestAborted).ConfigureAwait(false);
            if (row == null) { c.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        }
        if (row.Version != body.Version)
        {
            c.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteJson(c, new ErrorResponseBody { Error = "This draft changed. Refresh it before activating." }, Json(c));
            return;
        }
        var payload = DeserializeDefinitionPayload(row, c);
        if (payload?.Root == null)
        {
            c.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await WriteJson(c, new ErrorResponseBody { Error = "The saved draft payload is invalid." }, Json(c));
            return;
        }
        var activationToken = Guid.NewGuid();
        var pendingRootId = Guid.NewGuid();
        var claimedAt = DateTime.UtcNow;
        var claim = await store.TryClaimDraftActivationAsync(id, row.Version, activationToken, pendingRootId,
            claimedAt, c.RequestAborted).ConfigureAwait(false);
        if (claim == null)
        {
            c.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteJson(c, new ErrorResponseBody { Error = "Another session changed or activated this draft." }, Json(c));
            return;
        }
        var root = BuildChainEntity<TTimeTicker>(payload.Root, true, null, pendingRootId);
        var executionTime = body.ExecutionTime ?? payload.ExecutionTime ?? DateTime.UtcNow;
        root.ExecutionTime = executionTime < DateTime.UtcNow ? DateTime.UtcNow : executionTime;
        root.SchedulePolicyName = payload.SchedulePolicyName;
        root.BlockedOccurrenceBehavior = payload.BlockedOccurrenceBehavior;
        root.MissedExecutionAction = payload.MissedExecutionAction ?? MissedExecutionAction.RunOnce;
        root.MaximumLateness = ToMaximumLateness(payload.MaximumLatenessSeconds);
        var createdCount = 1 + CountDescendants(root);
        var result = await c.RequestServices.GetRequiredService<ITimeTickerManager<TTimeTicker>>()
            .AddAsync(root, c.RequestAborted).ConfigureAwait(false);
        if (!result.IsSucceeded || result.Result == null)
        {
            var committed = await persistence.GetTimeTickerById(pendingRootId, c.RequestAborted).ConfigureAwait(false);
            if (committed == null)
            {
                await store.ReleaseDraftActivationAsync(id, activationToken, c.RequestAborted).ConfigureAwait(false);
                await WriteTickerError(c, result.Exception);
                return;
            }
        }
        await store.CompleteDraftActivationAsync(id, activationToken, pendingRootId, DateTime.UtcNow, c.RequestAborted)
            .ConfigureAwait(false);
        await WriteJson(c, new AddTimeTickerChainResponseBody
        {
            RootId = pendingRootId.ToString(), CreatedCount = createdCount,
        }, Json(c));
    }

    private static bool TryGetDefinitionStore<TTimeTicker, TCronTicker>(HttpContext c,
        out ITickerDefinitionStore store)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        store = c.RequestServices.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>()
            as ITickerDefinitionStore;
        if (store != null) return true;
        c.Response.StatusCode = StatusCodes.Status501NotImplemented;
        return false;
    }

    private static bool ValidateDefinitionRequest(SaveTickerDefinitionRequest body, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Trim().Length > 200)
            error = "A template or draft name between 1 and 200 characters is required.";
        else if (body.Description?.Length > 2000)
            error = "The description cannot exceed 2000 characters.";
        else if (body.Payload?.Root == null)
            error = "A root job is required.";
        else if (body.Kind == SavedTickerDefinitionKind.Template &&
                 body.Payload.Root.Children is not { Count: > 0 })
            error = "Templates are available only for chains with at least two jobs.";
        else if (body.Kind == SavedTickerDefinitionKind.Draft && body.Target != SavedTickerDefinitionTarget.TimeTicker)
            error = "Drafts are supported only for time tickers. Save a cron configuration as a template instead.";
        return error == null;
    }

    private static async Task<bool> EnsureTemplateNameAvailableAsync(HttpContext c, ITickerDefinitionStore store,
        SavedTickerDefinitionKind kind, SavedTickerDefinitionTarget target, string name, Guid? excludedId)
    {
        if (kind != SavedTickerDefinitionKind.Template) return true;
        var normalized = SavedTickerDefinitionNames.Normalize(name);
        var definitions = await store.ListDefinitionsAsync(
            SavedTickerDefinitionKind.Template, target, c.RequestAborted).ConfigureAwait(false);
        if (!definitions.Any(row => row.Id != excludedId &&
                SavedTickerDefinitionNames.Normalize(row.Name) == normalized)) return true;
        await WriteDefinitionNameConflictAsync(c,
            $"A template named ‘{name.Trim()}’ already exists.").ConfigureAwait(false);
        return false;
    }

    private static async Task WriteDefinitionNameConflictAsync(HttpContext c, string message)
    {
        c.Response.StatusCode = StatusCodes.Status409Conflict;
        await WriteJson(c, new ErrorResponseBody { Error = message }, Json(c));
    }

    private static SavedDefinitionPayload CopyPayload(SavedDefinitionPayload payload, bool includeRequestData) => new()
    {
        ExecutionTime = payload.ExecutionTime,
        Expression = payload.Expression,
        SkipIfAlreadyRunning = payload.SkipIfAlreadyRunning,
        TimeZoneId = payload.TimeZoneId,
        SchedulePolicyName = payload.SchedulePolicyName,
        BlockedOccurrenceBehavior = payload.BlockedOccurrenceBehavior,
        MissedExecutionAction = payload.MissedExecutionAction,
        MaximumLatenessSeconds = payload.MaximumLatenessSeconds,
        CatchUpMode = payload.CatchUpMode,
        Root = payload.Root == null ? null : CopyNode(payload.Root, includeRequestData),
    };

    private static TimeSpan? ToMaximumLateness(long? seconds)
    {
        if (!seconds.HasValue) return null;
        if (seconds.Value < 0 || seconds.Value > (long)TimeSpan.MaxValue.TotalSeconds)
            return TimeSpan.FromTicks(-1); // manager validation returns a stable 422 response
        return TimeSpan.FromSeconds(seconds.Value);
    }

    private static long? ToMaximumLatenessSeconds(TimeSpan? value)
        => value.HasValue ? checked((long)value.Value.TotalSeconds) : null;

    private static TimeTickerNodeRequest CopyNode(TimeTickerNodeRequest node, bool includeRequestData) => new()
    {
        DeleteBehavior = node.DeleteBehavior, Function = node.Function, Description = node.Description,
        Retries = node.Retries, Request = includeRequestData ? node.Request?.ToArray() : null,
        RetryIntervalsSeconds = node.RetryIntervalsSeconds?.ToArray(), RunCondition = node.RunCondition,
        OnStale = node.OnStale, TimeoutSeconds = node.TimeoutSeconds,
        Children = node.Children?.Select(child => CopyNode(child, includeRequestData)).ToList(),
    };

    private static TimeTickerNodeRequest ToNodeRequest<TTimeTicker>(TTimeTicker node, bool includeRequestData)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new() => new()
    {
        DeleteBehavior = node.DeleteBehavior, Function = node.Function, Description = node.Description,
        Retries = node.Retries, Request = includeRequestData ? node.Request?.ToArray() : null,
        RetryIntervalsSeconds = node.RetryIntervals?.ToArray(), RunCondition = node.RunCondition,
        OnStale = node.OnStale, TimeoutSeconds = node.TimeoutSeconds,
        Children = node.GetChildrenForRead().Select(child => ToNodeRequest(child, includeRequestData)).ToList(),
    };

    private static TimeTickerNodeRequest ToNodeRequest(CronChainNode node, bool includeRequestData) => new()
    {
        Function = node.Function, Description = node.Description, Retries = node.Retries,
        Request = includeRequestData ? node.Request?.ToArray() : null,
        RetryIntervalsSeconds = node.RetryIntervals?.ToArray(), RunCondition = node.RunCondition,
        OnStale = node.OnStale, TimeoutSeconds = node.TimeoutSeconds,
        Children = node.Children?.Select(child => ToNodeRequest(child, includeRequestData)).ToList(),
    };

    private static SavedTickerDefinitionResponse ToDefinitionResponse(SavedTickerDefinition row, HttpContext c) => new()
    {
        Id = row.Id, Kind = row.Kind, Target = row.Target, Name = row.Name, Description = row.Description,
        IncludesRequestData = row.IncludesRequestData, SchemaVersion = row.SchemaVersion,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc, Version = row.Version,
        ActivatedRootTickerId = row.ActivatedRootTickerId, ActivatedAtUtc = row.ActivatedAtUtc,
        ActivationInProgress = row.ActivationToken.HasValue,
        Payload = DeserializeDefinitionPayload(row, c),
    };

    private static SavedDefinitionPayload DeserializeDefinitionPayload(SavedTickerDefinition row, HttpContext c)
    {
        try
        {
            return JsonSerializer.Deserialize(
                row.DefinitionJson,
                DashboardJsonSerializerContext.Default.SavedDefinitionPayload);
        }
        catch (JsonException) { return null; }
    }

    // ===== Chain helpers (ported from DashboardOperationGrpcService) =====

    private static TTimeTicker BuildChainEntity<TTimeTicker>(TimeTickerNodeRequest node, bool isRoot,
        Guid? parentId, Guid? forcedId = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        var entity = new TTimeTicker
        {
            Id = forcedId ?? Guid.NewGuid(),
            Function = node.Function,
            Description = node.Description,
            Retries = node.Retries ?? 0,
            Request = node.Request,
            RetryIntervals = node.RetryIntervalsSeconds,
            OnStale = node.OnStale ?? StaleAction.Restart,
            DeleteBehavior = node.DeleteBehavior ?? ChainDeleteBehavior.Cascade,
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
            DeleteBehavior = src.DeleteBehavior,
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
