using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = [], ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddSingleton<InvocationProbe>();
builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("installed-package-smoke");
    options.UseReconciliationEpoch(1);
    options.DisableBackgroundServices();
    options.UseLicense(Path.Combine(AppContext.BaseDirectory, "intentionally-missing.tqlicense"));
    options.WithJsonContext(SmokeJsonContext.Default);
});
using var host = builder.Build();
host.UseTickerQ();
await host.StartAsync();
try
{
    // Starting an ordinary installed-package host must remain fail-closed without a license.
    var license = host.Services.GetRequiredService<TickerQLicenseStateProvider>();
    Check(license.Current.Status == TickerQLicenseStatus.Missing && !license.ExecutionAllowed,
        "Missing-license startup did not remain fail-closed.");
    Check(!host.Services.GetRequiredService<ITickerQHostScheduler>().IsRunning,
        "Queue-only smoke unexpectedly started the scheduler.");
    _ = host.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    _ = host.Services.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
    _ = host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
    Check(TickerFunctionProvider.IsBuilt, "Queue-only DI did not publish the function registry.");
    Check(TickerFunctionProvider.TickerFunctions.ContainsKey("PackageSmoke.Run"),
        "The analyzer embedded in the installed TickerQ package did not register the job.");

    var requestInfo = TickerFunctionProvider.GetRequestTypeInfo<SmokeRequest>("PackageSmoke.Request");
    var bytes = TickerHelper.CreateTickerRequest(new SmokeRequest("installed-package", 42), requestInfo);
    Check(TickerHelper.ReadTickerRequest(bytes, requestInfo) is { Name: "installed-package", Count: 42 },
        "Source-generated request metadata round-trip failed.");
    Check(TickerRequestPayloadValidator.Validate("PackageSmoke.Request", bytes).IsValid,
        "Generated request contract rejected a valid payload.");
    Check(TickerFunctionProvider.GetResultTypeInfo<SmokeResult>("PackageSmoke.Run").Type == typeof(SmokeResult),
        "Generated result metadata was unavailable.");
    Check(TickerFunctionProvider.GetResultContract("PackageSmoke.Run").ContractId.StartsWith("sha256:", StringComparison.Ordinal),
        "Generated result contract has no canonical fingerprint.");

    Check(host.Services.GetRequiredService<InvocationProbe>().Calls == 0, "A scheduler executed a job during the smoke.");

    // Compiler/DI probe only: invoke the generated delegate directly, without scheduling a job,
    // altering license state, granting execution admission, or using internal test hooks.
    await TickerFunctionProvider.TickerFunctions["PackageSmoke.Run"].Delegate(
        CancellationToken.None, host.Services, new TickerFunctionContext());
    Check(host.Services.GetRequiredService<InvocationProbe>().Calls == 1,
        "Generated constructor/delegate did not resolve and call the consumer job.");
    Console.WriteLine("PACKAGE_SMOKE_CORE_OK");
}
finally { await host.StopAsync(); }

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

public sealed class InvocationProbe { public int Calls { get; set; } }

public sealed class SmokeJobs
{
    private readonly InvocationProbe _probe;
    public SmokeJobs(InvocationProbe probe) => _probe = probe;

    [TickerFunction("PackageSmoke.Run", ResultType = typeof(SmokeResult))]
    public ValueTask<SmokeResult> Run(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.Calls++;
        return ValueTask.FromResult(new SmokeResult("generated-delegate"));
    }

    [TickerFunction("PackageSmoke.Request")]
    public Task Request(TickerFunctionContext<SmokeRequest> context) => Task.CompletedTask;
}

public sealed record SmokeRequest(string Name, int Count);
public sealed record SmokeResult(string Value);

[JsonSerializable(typeof(SmokeRequest))]
[JsonSerializable(typeof(SmokeResult))]
internal partial class SmokeJsonContext : JsonSerializerContext;
