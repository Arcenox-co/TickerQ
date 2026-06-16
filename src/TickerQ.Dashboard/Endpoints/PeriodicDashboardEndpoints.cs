using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Dashboard.Endpoints;

#pragma warning disable IL2026
#pragma warning disable IL3050
internal static class PeriodicDashboardEndpoints
{
    /// <summary>
    /// Maps the optional periodic-ticker dashboard endpoints. Called only when
    /// <see cref="DashboardOptionsBuilder.PeriodicEnabled"/> is true.
    /// </summary>
    internal static void MapPeriodicEndpoints<TPeriodicTicker>(this RouteGroupBuilder apiGroup)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        apiGroup.MapGet("/periodic-tickers", GetPeriodicTickers<TPeriodicTicker>)
            .WithName("GetPeriodicTickers").WithSummary("Get all periodic tickers");

        apiGroup.MapGet("/periodic-tickers/paginated", GetPeriodicTickersPaginated<TPeriodicTicker>)
            .WithName("GetPeriodicTickersPaginated").WithSummary("Get paginated periodic tickers");

        apiGroup.MapGet("/periodic-tickers/graph-data", GetPeriodicTickersGraphData<TPeriodicTicker>)
            .WithName("GetPeriodicTickersGraphData").WithSummary("Get periodic tickers status counts");

        apiGroup.MapGet("/periodic-tickers/graph-data-range", GetPeriodicTickersGraphDataRange<TPeriodicTicker>)
            .WithName("GetPeriodicTickersGraphDataRange").WithSummary("Get periodic tickers graph data for date range");

        apiGroup.MapGet("/periodic-tickers/graph-data-range-id", GetPeriodicTickerByIdGraphDataRange<TPeriodicTicker>)
            .WithName("GetPeriodicTickerByIdGraphDataRange").WithSummary("Get periodic ticker graph data by ID for date range");

        apiGroup.MapGet("/periodic-ticker-occurrences/{periodicTickerId}", GetPeriodicTickerOccurrences<TPeriodicTicker>)
            .WithName("GetPeriodicTickerOccurrences").WithSummary("Get periodic ticker occurrences");

        apiGroup.MapGet("/periodic-ticker-occurrences/{periodicTickerId}/paginated", GetPeriodicTickerOccurrencesPaginated<TPeriodicTicker>)
            .WithName("GetPeriodicTickerOccurrencesPaginated").WithSummary("Get paginated periodic ticker occurrences");

        apiGroup.MapGet("/periodic-ticker-occurrences/{periodicTickerId}/graph-data", GetPeriodicTickerOccurrencesGraphData<TPeriodicTicker>)
            .WithName("GetPeriodicTickerOccurrencesGraphData").WithSummary("Get periodic ticker occurrences graph data");

        apiGroup.MapPost("/periodic-ticker/add", AddPeriodicTicker<TPeriodicTicker>)
            .WithName("AddPeriodicTicker").WithSummary("Add periodic ticker");

        apiGroup.MapPut("/periodic-ticker/update", UpdatePeriodicTicker<TPeriodicTicker>)
            .WithName("UpdatePeriodicTicker").WithSummary("Update periodic ticker");

        apiGroup.MapPut("/periodic-ticker/toggle", TogglePeriodicTicker<TPeriodicTicker>)
            .WithName("TogglePeriodicTicker").WithSummary("Toggle periodic ticker active state");

        apiGroup.MapPut("/periodic-ticker/pause", PausePeriodicTicker<TPeriodicTicker>)
            .WithName("PausePeriodicTicker").WithSummary("Pause periodic ticker");

        apiGroup.MapPut("/periodic-ticker/resume", ResumePeriodicTicker<TPeriodicTicker>)
            .WithName("ResumePeriodicTicker").WithSummary("Resume periodic ticker");

        apiGroup.MapDelete("/periodic-ticker/delete", DeletePeriodicTicker<TPeriodicTicker>)
            .WithName("DeletePeriodicTicker").WithSummary("Delete periodic ticker");

        apiGroup.MapDelete("/periodic-ticker/delete-batch", DeletePeriodicTickersBatch<TPeriodicTicker>)
            .WithName("DeletePeriodicTickersBatch").WithSummary("Delete multiple periodic tickers");

        apiGroup.MapDelete("/periodic-ticker-occurrence/delete", DeletePeriodicTickerOccurrence<TPeriodicTicker>)
            .WithName("DeletePeriodicTickerOccurrence").WithSummary("Delete periodic ticker occurrence");

        apiGroup.MapGet("/periodic-ticker-request/{id}", GetPeriodicTickerRequest<TPeriodicTicker>)
            .WithName("GetPeriodicTickerRequest").WithSummary("Get periodic ticker request payload");
    }

    private static Task WriteJson<T>(HttpContext context, T value, JsonSerializerOptions options)
        => Results.Json(value, options.GetTypeInfo(typeof(T))).ExecuteAsync(context);

    private static async Task GetPeriodicTickers<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var result = await repo.GetPeriodicTickersAsync(context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetPeriodicTickersPaginated<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        int.TryParse(context.Request.Query["pageNumber"].ToString(), out var pageNumber);
        if (pageNumber < 1) pageNumber = 1;
        int.TryParse(context.Request.Query["pageSize"].ToString(), out var pageSize);
        if (pageSize < 1) pageSize = 20;
        bool? isActive = bool.TryParse(context.Request.Query["isActive"].ToString(), out var ia) ? ia : null;
        var search = context.Request.Query["search"].ToString();

        var result = await repo.GetPeriodicTickersPaginatedAsync(pageNumber, pageSize, isActive, search, context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetPeriodicTickersGraphData<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var result = await repo.GetPeriodicTickerFullDataAsync(context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetPeriodicTickersGraphDataRange<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        if (!int.TryParse(context.Request.Query["pastDays"].ToString(), out var pastDays)) pastDays = -3;
        if (!int.TryParse(context.Request.Query["futureDays"].ToString(), out var futureDays)) futureDays = 3;
        var result = await repo.GetPeriodicTickersGraphSpecificDataAsync(pastDays, futureDays, context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetPeriodicTickerByIdGraphDataRange<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());
        if (!int.TryParse(context.Request.Query["pastDays"].ToString(), out var pastDays)) pastDays = -3;
        if (!int.TryParse(context.Request.Query["futureDays"].ToString(), out var futureDays)) futureDays = 3;
        var result = await repo.GetPeriodicTickersGraphSpecificDataByIdAsync(id, pastDays, futureDays, context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetPeriodicTickerOccurrences<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.RouteValues["periodicTickerId"]?.ToString()!);
        var result = await repo.GetPeriodicTickerOccurrencesAsync(id, context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetPeriodicTickerOccurrencesPaginated<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.RouteValues["periodicTickerId"]?.ToString()!);
        int.TryParse(context.Request.Query["pageNumber"].ToString(), out var pageNumber);
        if (pageNumber < 1) pageNumber = 1;
        int.TryParse(context.Request.Query["pageSize"].ToString(), out var pageSize);
        if (pageSize < 1) pageSize = 20;
        var result = await repo.GetPeriodicTickerOccurrencesPaginatedAsync(id, pageNumber, pageSize, context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task GetPeriodicTickerOccurrencesGraphData<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.RouteValues["periodicTickerId"]?.ToString()!);
        var result = await repo.GetPeriodicTickerOccurrencesGraphDataAsync(id, context.RequestAborted);
        await WriteJson(context, result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task AddPeriodicTicker<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var manager = context.RequestServices.GetRequiredService<IPeriodicTickerManager<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(context.RequestAborted);
        var ticker = (TPeriodicTicker)JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TPeriodicTicker)));

        var result = await manager.AddAsync(ticker, context.RequestAborted);
        await WriteJson(context, new ActionResponseWithId
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Periodic ticker added successfully" : "Failed to add periodic ticker",
            TickerId = result.Result?.Id
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task UpdatePeriodicTicker<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var manager = context.RequestServices.GetRequiredService<IPeriodicTickerManager<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(context.RequestAborted);
        var ticker = (TPeriodicTicker)JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TPeriodicTicker)));
        ticker.Id = id;

        var result = await manager.UpdateAsync(ticker, context.RequestAborted);
        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Periodic ticker updated successfully" : "Failed to update periodic ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task TogglePeriodicTicker<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());
        bool.TryParse(context.Request.Query["isActive"].ToString(), out var isActive);

        var success = await repo.TogglePeriodicTickerAsync(id, isActive, context.RequestAborted);
        await WriteJson(context, new ActionResponse
        {
            Success = success,
            Message = success ? $"Periodic ticker {(isActive ? "activated" : "paused")} successfully" : "Failed to toggle periodic ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task PausePeriodicTicker<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var manager = context.RequestServices.GetRequiredService<IPeriodicTickerManager<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        var result = await manager.PauseAsync(id, context.RequestAborted);
        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Periodic ticker paused successfully" : "Failed to pause periodic ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task ResumePeriodicTicker<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var manager = context.RequestServices.GetRequiredService<IPeriodicTickerManager<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        var result = await manager.ResumeAsync(id, context.RequestAborted);
        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Periodic ticker resumed successfully" : "Failed to resume periodic ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task DeletePeriodicTicker<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var manager = context.RequestServices.GetRequiredService<IPeriodicTickerManager<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());

        var result = await manager.DeleteAsync(id, context.RequestAborted);
        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Periodic ticker deleted successfully" : "Failed to delete periodic ticker"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task DeletePeriodicTickersBatch<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var manager = context.RequestServices.GetRequiredService<IPeriodicTickerManager<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(context.RequestAborted);
        var ids = (Guid[])JsonSerializer.Deserialize(jsonString, dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(Guid[])));
        var idList = ids is { Length: > 0 } ? new List<Guid>(ids) : new List<Guid>();

        var result = await manager.DeleteBatchAsync(idList, context.RequestAborted);
        await WriteJson(context, new ActionResponse
        {
            Success = result.IsSucceeded,
            Message = result.IsSucceeded ? "Periodic tickers deleted successfully" : "Failed to delete periodic tickers"
        }, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task DeletePeriodicTickerOccurrence<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var id = Guid.Parse(context.Request.Query["id"].ToString());
        await repo.DeletePeriodicTickerOccurrenceByIdAsync(id, context.RequestAborted);
        context.Response.StatusCode = 200;
    }

    private static async Task GetPeriodicTickerRequest<TPeriodicTicker>(HttpContext context)
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        var repo = context.RequestServices.GetRequiredService<IPeriodicDashboardRepository<TPeriodicTicker>>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var id = Guid.Parse(context.Request.RouteValues["id"]?.ToString()!);
        var (json, matchType) = await repo.GetPeriodicTickerRequestByIdAsync(id, context.RequestAborted);

        await WriteJson(context, new TickerRequestResponse { Result = json, MatchType = matchType }, dashboardOptions.DashboardJsonOptions);
    }
}

