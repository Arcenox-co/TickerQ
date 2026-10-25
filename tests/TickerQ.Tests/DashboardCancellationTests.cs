using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public class DashboardCancellationTests : IDisposable
{
    private readonly ITimeTickerManager<TimeTickerEntity> _manager = Substitute.For<ITimeTickerManager<TimeTickerEntity>>();
    private readonly ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity> _data =
        Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
    private readonly ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> _persistence =
        Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

    public void Dispose() => TickerCancellationTokenManager.CleanUpTickerCancellationTokens();

    [Fact]
    public async Task PendingEndpoint_UsesManagerAndReportsCancelledDescendants()
    {
        var id = Guid.NewGuid();
        _manager.CancelAsync(id, Arg.Any<CancellationToken>()).Returns(new TickerResult<TimeTickerEntity>(3));
        using var host = await CreateHost();
        var response = await host.GetTestClient().PostAsync($"/api/dashboard/time-tickers/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", body.GetProperty("outcome").GetString());
        Assert.Equal(3, body.GetProperty("affected").GetInt32());
        await _data.DidNotReceiveWithAnyArgs().RequestRunningTickerCancellationAsync(default, default, default);
    }

    [Fact]
    public async Task PendingEndpoint_JobStartedSinceConfirmation_DoesNotSignalRunningToken()
    {
        var id = Guid.NewGuid();
        _manager.CancelAsync(id, Arg.Any<CancellationToken>()).Returns(new TickerResult<TimeTickerEntity>(0));
        _data.GetTimeTickerByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto { Id = id, Status = TickerStatus.InProgress });
        using var host = await CreateHost();
        var response = await host.GetTestClient().PostAsync($"/api/dashboard/time-tickers/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await _data.DidNotReceiveWithAnyArgs().RequestRunningTickerCancellationAsync(default, default, default);
    }

    [Theory]
    [InlineData("time-tickers", TickerType.TimeTicker, true)]
    [InlineData("cron-occurrences", TickerType.CronTickerOccurrence, true)]
    [InlineData("time-tickers", TickerType.TimeTicker, false)]
    [InlineData("cron-occurrences", TickerType.CronTickerOccurrence, false)]
    public async Task RunningEndpoint_ReportsRequestOrConflict_NotPrematureCompletion(string route, TickerType type, bool found)
    {
        var id = Guid.NewGuid();
        _data.RequestRunningTickerCancellationAsync(id, type, Arg.Any<CancellationToken>()).Returns(found);
        using var host = await CreateHost();
        var response = await host.GetTestClient().PostAsync($"/api/dashboard/{route}/{id}/request-cancellation", null);
        Assert.Equal(found ? HttpStatusCode.Accepted : HttpStatusCode.Conflict, response.StatusCode);
        if (found)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Requested", body.GetProperty("outcome").GetString());
        }
        await _manager.DidNotReceiveWithAnyArgs().CancelAsync(default, default);
    }

    [Theory]
    [InlineData("time-tickers", "cancel")]
    [InlineData("time-tickers", "request-cancellation")]
    [InlineData("cron-occurrences", "request-cancellation")]
    public async Task CancellationEndpoints_RespectReadOnlyDashboard(string route, string operation)
    {
        using var host = await CreateHost(readOnly: true);
        var response = await host.GetTestClient().PostAsync($"/api/dashboard/{route}/{Guid.NewGuid()}/{operation}", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await _manager.DidNotReceiveWithAnyArgs().CancelAsync(default, default);
        await _data.DidNotReceiveWithAnyArgs().RequestRunningTickerCancellationAsync(default, default, default);
    }

    [Theory]
    [InlineData("viewer", HttpStatusCode.Forbidden)]
    [InlineData("ops", HttpStatusCode.NoContent)]
    public async Task WriteEndpoints_EnforcePerUserReadOnlyPredicate(string role, HttpStatusCode expected)
    {
        using var host = await CreateHost(readOnlyWhen: context => !context.User.IsInRole("ops"), role: role);

        var response = await host.GetTestClient().PostAsync("/api/dashboard/host/restart", null);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task TimeTickerResultEndpoint_ReturnsPersistedEnvelopeAndMetadata()
    {
        var id = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("{\"invoiceId\":42}");
        _persistence.GetTimeTickerResultAsync(id, Arg.Any<CancellationToken>()).Returns(
            new TickerResultEnvelope(payload, 1, "application/json", "sha256:result", "InvoiceResult"));

        using var host = await CreateHost();
        var response = await host.GetTestClient().GetAsync($"/api/dashboard/time-tickers/{id}/result");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(payload, body.GetProperty("payload").GetBytesFromBase64());
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        Assert.Equal("application/json", body.GetProperty("mediaType").GetString());
        Assert.Equal("sha256:result", body.GetProperty("contractId").GetString());
        Assert.Equal("InvoiceResult", body.GetProperty("contractType").GetString());
    }

    [Theory]
    [InlineData(TickerType.TimeTicker, false)]
    [InlineData(TickerType.TimeTicker, true)]
    [InlineData(TickerType.CronTickerOccurrence, false)]
    public async Task RunningDataService_SignalsOnlyExactTypeNamespaceAndGeneration(TickerType type, bool child)
    {
        var id = Guid.NewGuid();
        var token = Guid.NewGuid();
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope("dashboard-cancel", 1, schedulerEnabled: false);
        var partition = options.RuntimePartition.StorageKey;
        var context = new InternalFunctionContext
        {
            TickerId = id, Type = type, RuntimePartitionKey = partition,
            ParentId = child || type == TickerType.CronTickerOccurrence ? Guid.NewGuid() : null,
            AcquisitionToken = token, ChainGeneration = child ? token : Guid.NewGuid()
        };
        var source = TickerCancellationTokenManager.TryRegisterAcquired(context, false)!;
        var otherNamespace = new InternalFunctionContext
        {
            TickerId = id, Type = type, AcquisitionToken = token,
            RuntimePartitionKey = new TickerQRuntimePartition("another-application").StorageKey
        };
        var otherSource = TickerCancellationTokenManager.TryRegisterAcquired(otherNamespace, false)!;
        var otherType = new InternalFunctionContext
        {
            TickerId = id, AcquisitionToken = token, RuntimePartitionKey = partition,
            Type = type == TickerType.TimeTicker ? TickerType.CronTickerOccurrence : TickerType.TimeTicker
        };
        var otherTypeSource = TickerCancellationTokenManager.TryRegisterAcquired(otherType, false)!;
        var persistence = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var ticker = new TimeTickerEntity
        {
            Id = id, Status = TickerStatus.InProgress, AcquisitionToken = token,
            ParentId = child ? context.ParentId : null, ChainGeneration = child ? token : null
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = id, Status = TickerStatus.InProgress, AcquisitionToken = token
        };
        persistence.GetTimeTickerById(id, Arg.Any<CancellationToken>()).Returns(ticker);
        persistence.CronTickerOccurrencesQuery().Returns(new InMemoryTickerQueryable<CronTickerOccurrenceEntity<CronTickerEntity>>(
            _ => Task.FromResult(new List<CronTickerOccurrenceEntity<CronTickerEntity>> { occurrence })));
        var service = new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(persistence, schedulerOptions: options);

        Assert.True(await service.RequestRunningTickerCancellationAsync(id, type));
        Assert.True(source.IsCancellationRequested);
        Assert.False(otherSource.IsCancellationRequested);
        Assert.False(otherTypeSource.IsCancellationRequested);
        Assert.Equal(TickerStatus.InProgress, ticker.Status);
        Assert.Equal(TickerStatus.InProgress, occurrence.Status);

        ticker.AcquisitionToken = ticker.ChainGeneration = occurrence.AcquisitionToken = Guid.NewGuid();
        Assert.False(await service.RequestRunningTickerCancellationAsync(id, type));
        ticker.Status = occurrence.Status = TickerStatus.Done;
        Assert.False(await service.RequestRunningTickerCancellationAsync(id, type));
    }

    private async Task<IHost> CreateHost(bool readOnly = false,
        Func<Microsoft.AspNetCore.Http.HttpContext, bool> readOnlyWhen = null, string role = null)
    {
        var options = new DashboardOptionsBuilder().AllowAnonymousDashboard().SetReadOnly(readOnly);
        if (readOnlyWhen != null) options.SetReadOnly(readOnlyWhen);
        options.DashboardJsonOptions = new JsonSerializerOptions(DashboardJsonSerializerContext.Default.Options);
        return await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddCors(cors => cors.AddPolicy("TickerQ_Dashboard_CORS", p => p.AllowAnyOrigin()));
                services.AddSingleton(options);
                services.AddSingleton(LicenseTestState.Active());
                services.AddSingleton(_manager);
                services.AddSingleton(_data);
                services.AddSingleton(_persistence);
            })
            .Configure(app =>
            {
                if (role != null)
                    app.Use((context, next) =>
                    {
                        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)], "test"));
                        return next(context);
                    });
                app.UseRouting();
                app.UseCors();
                app.UseEndpoints(endpoints => endpoints.MapDashboardEndpoints<TimeTickerEntity, CronTickerEntity>(options));
            })).StartAsync();
    }
}
