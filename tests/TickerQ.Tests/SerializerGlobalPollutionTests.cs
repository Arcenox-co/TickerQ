using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TickerQ.Dashboard;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class SerializerGlobalPollutionTests
{
    private static (IServiceProvider services, DashboardOptionsBuilder options) BuildServicesWithDashboard(
        bool registerUserHttpResolver = false,
        IJsonTypeInfoResolver? userResolver = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        if (registerUserHttpResolver && userResolver != null)
        {
            builder.Services.ConfigureHttpJsonOptions(opts =>
                opts.SerializerOptions.TypeInfoResolverChain.Add(userResolver));
        }
        var dashboardOptions = new DashboardOptionsBuilder();
        builder.Services.AddDashboardService<TimeTickerEntity, CronTickerEntity>(dashboardOptions);
        var sp = builder.Services.BuildServiceProvider();
        return (sp, dashboardOptions);
    }

    private static IServiceProvider BuildServicesWithoutDashboard()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Services.AddSignalR();
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void AddDashboardService_ShouldNotPollute_GlobalSignalR_WithDashboardContext()
    {
        var (sp, _) = BuildServicesWithDashboard();
        var hubProtocolOptions = sp.GetRequiredService<IOptions<JsonHubProtocolOptions>>();
        var chain = hubProtocolOptions.Value.PayloadSerializerOptions.TypeInfoResolverChain;

        Assert.DoesNotContain(chain, resolver => ReferenceEquals(resolver, DashboardJsonSerializerContext.Default));
    }

    [Fact]
    public void AddDashboardService_AppliesHostSignalRConfiguration()
    {
        var invoked = 0;
        var options = new DashboardOptionsBuilder()
            .ConfigureSignalR(signalR =>
            {
                invoked++;
                signalR.Services.AddSingleton<SignalRConfigurationMarker>();
            });
        var services = new ServiceCollection();

        services.AddDashboardService<TimeTickerEntity, CronTickerEntity>(options);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(1, invoked);
        Assert.NotNull(provider.GetService<SignalRConfigurationMarker>());
    }

    private sealed class SignalRConfigurationMarker { }

    [Fact]
    public void AddDashboard_ReplacesCoreNoOpNotificationSenderWithoutRegisteringServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddTickerQ(options => options
            .DisableBackgroundServices()
            .AddDashboard(dashboard => dashboard.AllowAnonymousDashboard()));

        var sender = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ITickerQNotificationHubSender));
        Assert.Equal(typeof(TickerQNotificationHubSender), sender.ImplementationType);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IServiceCollection));
    }

    [Fact]
    public void AddDashboardService_GlobalJsonHubProtocolOptions_ShouldNotContainDashboardContext()
    {
        var (sp, _) = BuildServicesWithDashboard();
        var hubProtocolOptions = sp.GetRequiredService<IOptions<JsonHubProtocolOptions>>();
        var chain = hubProtocolOptions.Value.PayloadSerializerOptions.TypeInfoResolverChain;

        Assert.DoesNotContain(chain, r => ReferenceEquals(r, DashboardJsonSerializerContext.Default));
    }

    [Fact]
    public void AddDashboardService_HttpJsonOptions_ShouldNotInsertResolverAtPositionZero()
    {
        var (sp, _) = BuildServicesWithDashboard();
        var httpJsonOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        var chain = httpJsonOptions.Value.SerializerOptions.TypeInfoResolverChain;

        Assert.False(
            chain.Count > 0 && ReferenceEquals(chain[0], DashboardJsonSerializerContext.Default),
            "DashboardJsonSerializerContext.Default should NOT be at position 0 of TypeInfoResolverChain");
    }

    [Fact]
    public void AddDashboardService_HttpJsonOptions_UserResolverIsPreserved()
    {
        var userResolver = new DefaultJsonTypeInfoResolver();
        var (sp, _) = BuildServicesWithDashboard(registerUserHttpResolver: true, userResolver: userResolver);
        var httpJsonOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        var chain = httpJsonOptions.Value.SerializerOptions.TypeInfoResolverChain;

        Assert.Contains(chain, r => ReferenceEquals(r, userResolver));
        Assert.DoesNotContain(chain, r => ReferenceEquals(r, DashboardJsonSerializerContext.Default));
    }

    [Fact]
    public void WithoutAddDashboardService_GlobalJsonHubProtocolOptions_DoesNotContainDashboardContext()
    {
        // Control test — verifies baseline: no dashboard context without AddDashboardService.
        var sp = BuildServicesWithoutDashboard();
        var hubProtocolOptions = sp.GetRequiredService<IOptions<JsonHubProtocolOptions>>();
        var chain = hubProtocolOptions.Value.PayloadSerializerOptions.TypeInfoResolverChain;

        Assert.DoesNotContain(chain, r => ReferenceEquals(r, DashboardJsonSerializerContext.Default));
    }

    [Fact]
    public void DashboardJsonOptions_CanSerialize_CronTickerEntity()
    {
        var (_, dashboardOptions) = BuildServicesWithDashboard();
        Assert.NotNull(dashboardOptions.DashboardJsonOptions);

        var entity = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Expression = "* * * * *"
        };

        var json = JsonSerializer.Serialize(
            entity,
            dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(CronTickerEntity)));

        Assert.False(string.IsNullOrEmpty(json));
        Assert.Contains("\"expression\"", json);
    }

    [Fact]
    public void DashboardJsonOptions_CanSerialize_TimeTickerEntity()
    {
        var (_, dashboardOptions) = BuildServicesWithDashboard();
        Assert.NotNull(dashboardOptions.DashboardJsonOptions);

        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid()
        };

        var json = JsonSerializer.Serialize(
            entity,
            dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TimeTickerEntity)));

        Assert.False(string.IsNullOrEmpty(json));
        Assert.Contains("\"id\"", json);
    }

    [Fact]
    public void DashboardJsonOptions_CamelCase_NamingPolicy_IsApplied()
    {
        var (_, dashboardOptions) = BuildServicesWithDashboard();

        var entity = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Expression = "0 * * * *"
        };

        var json = JsonSerializer.Serialize(
            entity,
            dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(CronTickerEntity)));

        Assert.DoesNotContain("\"Expression\"", json);
        Assert.Contains("\"expression\"", json);
    }

    [Fact]
    public void DashboardJsonOptions_CanSerialize_PaginationResult()
    {
        var (_, dashboardOptions) = BuildServicesWithDashboard();
        Assert.NotNull(dashboardOptions.DashboardJsonOptions);

        var result = new PaginationResult<CronTickerEntity>(
            new[]
            {
                new CronTickerEntity { Id = Guid.NewGuid(), Expression = "* * * * *" },
                new CronTickerEntity { Id = Guid.NewGuid(), Expression = "0 0 * * *" }
            },
            totalCount: 2,
            pageNumber: 1,
            pageSize: 10);

        var json = JsonSerializer.Serialize(
            result,
            dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(PaginationResult<CronTickerEntity>)));

        Assert.False(string.IsNullOrEmpty(json));
        Assert.Contains("\"items\"", json);
        Assert.Contains("\"totalCount\"", json);
    }

    [Fact]
    public void DashboardJsonOptions_CanSerialize_Guid_AndEnum()
    {
        var (_, dashboardOptions) = BuildServicesWithDashboard();
        Assert.NotNull(dashboardOptions.DashboardJsonOptions);

        var id = Guid.NewGuid();
        var guidJson = JsonSerializer.Serialize(
            id,
            dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(Guid)));
        Assert.False(string.IsNullOrEmpty(guidJson));

        // Roundtrip: deserialize back and verify equality.
        var roundtripped = JsonSerializer.Deserialize<Guid>(guidJson);
        Assert.Equal(id, roundtripped);

        // Enum serialization — TickerStatus.Done
        var status = TickerStatus.Done;
        var statusJson = JsonSerializer.Serialize(
            status,
            dashboardOptions.DashboardJsonOptions.GetTypeInfo(typeof(TickerStatus)));
        Assert.False(string.IsNullOrEmpty(statusJson));
    }

    [Fact]
    public void DashboardJsonOptions_IsIsolated_FromGlobalHttpJsonOptions()
    {
        var (sp, dashboardOptions) = BuildServicesWithDashboard();

        var globalHttpJsonOptions = sp
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value
            .SerializerOptions;

        Assert.NotNull(dashboardOptions.DashboardJsonOptions);
        Assert.NotSame(globalHttpJsonOptions, dashboardOptions.DashboardJsonOptions);
    }
}
