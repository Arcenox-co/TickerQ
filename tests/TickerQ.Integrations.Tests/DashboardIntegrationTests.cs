using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Tests;

public class DashboardIntegrationTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public async Task Writes_RequireManagementPermission_WritableDashboard_AndLicense(bool permission, bool readOnly, bool licensed)
    {
        var service = new FakeAdmin();
        using var host = await Host(service, permission, readOnly, licensed);
        var http = host.GetTestClient();
        foreach (var path in new[] { "integrations/preview", $"integrations/destinations/{Guid.NewGuid()}/test" })
        {
            var response = await http.PostAsJsonAsync("/api/dashboard/" + path, new { provider = "webhook" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync("/api/dashboard/integrations", new IntegrationConfiguration())).StatusCode);
        Assert.Equal(0, service.Mutations);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Viewers_ReadHealthAndDeliveries_WithoutConfigurationSecrets(bool permission, bool readOnly)
    {
        var service = new FakeAdmin(); using var host = await Host(service, permission, readOnly);
        var response = await host.GetTestClient().GetFromJsonAsync<JsonElement>("/api/dashboard/integrations");
        Assert.True(response.GetProperty("available").GetBoolean());
        Assert.False(response.GetProperty("canManage").GetBoolean());
        var destination = response.GetProperty("configuration").GetProperty("destinations")[0];
        Assert.Equal("Team alerts", destination.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, destination.GetProperty("url").ValueKind);
        Assert.Equal(JsonValueKind.Null, destination.GetProperty("headers").ValueKind);
        Assert.Equal(JsonValueKind.Null, destination.GetProperty("payloadTemplate").ValueKind);
        Assert.Equal(JsonValueKind.Null, destination.GetProperty("messageTemplates").ValueKind);
        Assert.Equal(JsonValueKind.Null, destination.GetProperty("endpointHost").ValueKind);
        Assert.Equal("incident", response.GetProperty("monitors")[0].GetProperty("status").GetString());
        Assert.Equal("Failed", response.GetProperty("recentDeliveries")[0].GetProperty("outcome").GetString());
        Assert.Equal("", response.GetProperty("recentDeliveries")[0].GetProperty("message").GetString());
        Assert.Equal(1, service.Reads);
        Assert.Equal(0, service.Mutations);
    }

    [Fact]
    public async Task AuthorizedEndpoints_UseSourceGeneratedJson_AndReportConflicts()
    {
        var service = new FakeAdmin(); using var host = await Host(service, true);
        var http = host.GetTestClient();
        var snapshot = await http.GetFromJsonAsync<JsonElement>("/api/dashboard/integrations");
        Assert.True(snapshot.GetProperty("canManage").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await http.PutAsJsonAsync("/api/dashboard/integrations", new IntegrationConfiguration())).StatusCode);
        var preview = await http.PostAsJsonAsync("/api/dashboard/integrations/preview", new { provider = "webhook" });
        Assert.Equal("{}", (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("payload").GetString());
        var test = await http.PostAsJsonAsync($"/api/dashboard/integrations/destinations/{Guid.NewGuid()}/test", new { });
        Assert.Equal("Sent", (await test.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        service.Conflict = true;
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsJsonAsync("/api/dashboard/integrations", new IntegrationConfiguration())).StatusCode);
    }

    [Fact]
    public async Task Writes_RejectFormPosts_AndOversizedSettings()
    {
        var service = new FakeAdmin(); using var host = await Host(service, true);
        var http = host.GetTestClient();
        var form = new StringContent("provider=webhook", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await http.PostAsync("/api/dashboard/integrations/preview", form)).StatusCode);
        var oversized = new StringContent(new string(' ', 256 * 1024 + 1), System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.PutAsync("/api/dashboard/integrations", oversized)).StatusCode);
        Assert.Equal(0, service.Mutations);
    }

    private static async Task<IHost> Host(FakeAdmin service, bool permission, bool readOnly = false, bool licensed = true)
    {
        var config = new DashboardOptionsBuilder().AllowAnonymousDashboard().SetReadOnly(readOnly).AllowIntegrationManagement(_ => permission);
        var license = new TickerQLicenseStateProvider(new Clock());
        if (licensed) license.Publish(TickerQLicenseState.ForCertificate(TickerQLicenseStatus.Active, false, Guid.NewGuid(), Guid.NewGuid(), "Test", "Business", "Full", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1), 365, 4, "Ed25519", "test", "5.x"));
        return await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddRouting(); services.AddCors(o => o.AddPolicy("TickerQ_Dashboard_CORS", p => p.AllowAnyOrigin()));
            services.AddSingleton(config); services.AddSingleton(license); services.AddSingleton<ITickerIntegrationAdminService>(service);
        }).Configure(app => { app.UseRouting(); app.UseCors(); app.UseEndpoints(e => e.MapDashboardEndpoints<TimeTickerEntity, CronTickerEntity>(config)); })).StartAsync();
    }
    private sealed class Clock : ITickerClock { public DateTime UtcNow => DateTime.UtcNow; }
    private sealed class FakeAdmin : ITickerIntegrationAdminService
    {
        public int Mutations, Reads; public bool Conflict;
        public Task<IntegrationSnapshot> GetAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(new IntegrationSnapshot
            {
                Available = true, MonitoringAvailable = true,
                Configuration = new IntegrationConfiguration { Destinations = [new IntegrationDestination
                {
                    Name = "Team alerts", Url = "https://private.example/secret", Headers = new() { ["Authorization"] = "secret" },
                    PayloadTemplate = "secret-template", MessageTemplates = new() { Recovery = "secret" }, EndpointHost = "private.example"
                }] },
                Monitors = [new IntegrationMonitorStatus { Status = "incident" }],
                RecentDeliveries = [new IntegrationDeliveryStatus { Outcome = "Failed", Message = "private-details" }]
            });
        }
        public Task SaveAsync(IntegrationConfiguration configuration, CancellationToken cancellationToken = default) { Mutations++; if (Conflict) throw new IntegrationConfigurationConflictException(); return Task.CompletedTask; }
        public Task<IntegrationDeliveryStatus> TestAsync(Guid id, CancellationToken cancellationToken = default) { Mutations++; return Task.FromResult(new IntegrationDeliveryStatus { DestinationId = id, Outcome = "Sent" }); }
        public IntegrationPreviewResponse Preview(IntegrationPreviewRequest request) { Mutations++; return new() { Payload = "{}" }; }
    }
}
