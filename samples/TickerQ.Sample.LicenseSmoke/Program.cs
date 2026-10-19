using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ.Sample.LicenseSmoke;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--help"))
            {
                Console.WriteLine("Usage: --license <external .tqlicense path> [--suite | --dashboard] [--expect valid|Missing|Invalid]");
                Console.WriteLine("Dashboard only: --example basic|retention|readonly|database|chains (default basic), --port 5217");
                Console.WriteLine("Alternatively set TICKERQ_LICENSE_PATH. No certificate contents or customer identifiers are printed.");
                return 0;
            }
            string? path = Environment.GetEnvironmentVariable("TICKERQ_LICENSE_PATH");
            var expected = "valid";
            var suite = false;
            var dashboard = false;
            string? example = null;
            int? port = null;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--license" when i + 1 < args.Length: path = args[++i]; break;
                    case "--expect" when i + 1 < args.Length: expected = args[++i]; break;
                    case "--suite": suite = true; break;
                    case "--dashboard": dashboard = true; break;
                    case "--example" when i + 1 < args.Length: example = args[++i]; break;
                    case "--port" when i + 1 < args.Length:
                        Require(int.TryParse(args[++i], out var parsedPort) && parsedPort is >= 1024 and <= 65535,
                            "The preview port must be between 1024 and 65535.");
                        port = parsedPort;
                        break;
                    default: throw new InvalidOperationException("Unknown or incomplete option. Use --help.");
                }
            }
            Require(!string.IsNullOrWhiteSpace(path), "Provide --license or TICKERQ_LICENSE_PATH.");
            Require(expected is "valid" or "Missing" or "Invalid", "Expected status must be valid, Missing, or Invalid.");
            Require(!dashboard || (!suite && expected == "valid"), "Use --dashboard separately from --suite and --expect.");
            Require(dashboard || (example == null && port == null), "Use --example and --port only with --dashboard.");
            Require(example is null or "basic" or "retention" or "readonly" or "database" or "chains", "Choose the basic, retention, readonly, database, or chains example.");
            path = Path.GetFullPath(path!);
            if (dashboard) return await DashboardPreview.RunAsync(path, example ?? "basic", port ?? 5217);
            return suite ? await RunSuiteAsync(path) : await RunCaseAsync(path, expected);
        }
        catch (Exception exception)
        {
            // The certificate is untrusted input: never dump an exception, raw claims, or its bytes.
            Console.Error.WriteLine(exception is InvalidOperationException
                ? $"FAIL: {exception.Message}" : $"FAIL: {exception.GetType().Name}. Check the license path and local runtime.");
            return 1;
        }
    }

    private static async Task<int> RunCaseAsync(string path, string expected)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [], EnvironmentName = Environments.Development
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<ExecutionProbe>();
        builder.Services.AddTickerQ(options =>
        {
            options.UseLicense(path);
            options.UseApplicationNamespace("license-smoke-test");
            options.UseReconciliationEpoch(1);
            options.IgnoreSeedDefinedCronTickers(); // Preview cron functions must not affect the isolated smoke test.
        });
        using var host = builder.Build();
        host.UseTickerQ();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            await host.StartAsync(deadline.Token);
            var state = host.Services.GetRequiredService<TickerQLicenseStateProvider>().Current;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                libraryVersion = typeof(TickerQServiceExtensions).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                status = state.Status.ToString(), state.ExecutionAllowed, state.Plan, state.Kind,
                state.IsEvaluation, state.SchemaVersion, state.Algorithm, state.KeyId,
                state.ExpiresAt, state.AnchoredMinorLine
            }, new JsonSerializerOptions { WriteIndented = true }));

            if (expected != "valid")
            {
                Require(state.Status.ToString() == expected && !state.ExecutionAllowed,
                    $"Expected {expected} with execution blocked; received {state.Status}.");
                var scheduler = host.Services.GetRequiredService<ITickerQHostScheduler>();
                var dispatcher = host.Services.GetRequiredService<ITickerQDispatcher>();
                Require(!scheduler.IsRunning && !dispatcher.IsEnabled, "Unlicensed scheduler or dispatcher is enabled.");
                var rejected = false;
                try
                {
                    await dispatcher.DispatchAsync([new InternalFunctionContext
                    {
                        TickerId = Guid.NewGuid(), FunctionName = "LicenseSmoke"
                    }], deadline.Token);
                }
                catch (InvalidOperationException exception) when (exception.Message == state.Message)
                {
                    rejected = true;
                }
                Require(rejected, "Direct dispatch did not reject the unlicensed job.");
                Console.WriteLine($"PASS: {expected} certificate leaves the host available and blocks scheduling and dispatch.");
                return 0;
            }

            Require(state.Status is TickerQLicenseStatus.Active or TickerQLicenseStatus.Expiring,
                $"Certificate is {state.Status}: {state.Message}");
            Require(state.ExecutionAllowed, "The verified certificate does not permit execution.");
            var manager = host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
            var scheduled = await manager.AddAsync(new TimeTickerEntity
            {
                Function = "LicenseSmoke", ExecutionTime = DateTime.UtcNow.AddSeconds(2)
            }, deadline.Token);
            Require(scheduled.IsSucceeded, "Scheduling the smoke-test job failed.");
            var probe = host.Services.GetRequiredService<ExecutionProbe>();
            var executedId = await probe.Executed.Task.WaitAsync(deadline.Token);
            Require(executedId == scheduled.Result.Id, "The executed job does not match the scheduled job.");
            var store = host.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
            while (true)
            {
                var job = await store.GetTimeTickerById(executedId, deadline.Token);
                if (job.Status is TickerStatus.Done or TickerStatus.DueDone) break;
                Require(job.Status is not (TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped),
                    $"Job ended with unexpected status {job.Status}.");
                await Task.Delay(100, deadline.Token);
            }
            Require(probe.ExecutionCount == 1, "The smoke-test job executed more than once.");
            Console.WriteLine("PASS: Certificate verified with the library's trusted key; scheduled job executed and reached a completed status.");
            return 0;
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(stop.Token);
        }
    }

    private static async Task<int> RunSuiteAsync(string path)
    {
        Require(File.Exists(path), "The supplied certificate file does not exist.");
        Require(new FileInfo(path).Length <= 128 * 1024, "Certificate exceeds the library size limit.");
        var directory = Directory.CreateTempSubdirectory("tickerq-license-smoke-");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var malformed = Path.Combine(directory.FullName, "malformed.tqlicense");
            await File.WriteAllTextAsync(malformed, "not a certificate");
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))?.AsObject();
            var signature = envelope?["signature"]?.GetValue<string>();
            Require(!string.IsNullOrEmpty(signature), "Expected a portal JSON certificate with a signature.");
            envelope!["signature"] = (signature![0] == 'A' ? "B" : "A") + signature[1..];
            var tampered = Path.Combine(directory.FullName, "tampered.tqlicense");
            await File.WriteAllTextAsync(tampered, envelope.ToJsonString());
            var cases = new[]
            {
                ("Missing certificate", Path.Combine(directory.FullName, "absent.tqlicense"), "Missing"),
                ("Malformed certificate", malformed, "Invalid"),
                ("Modified signature", tampered, "Invalid"),
                ("Your original certificate", path, "valid")
            };
            var failures = 0;
            foreach (var (name, certificate, expected) in cases)
            {
                Console.WriteLine($"\n--- {name} ---");
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(Program).Assembly.Location);
                foreach (var value in new[] { "--license", certificate, "--expect", expected })
                    start.ArgumentList.Add(value);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start an isolated test process.");
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try { await process.WaitForExitAsync(limit.Token); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
                if (process.ExitCode != 0) failures++;
            }
            Console.WriteLine($"\n{cases.Length - failures}/{cases.Length} license smoke checks passed.");
            return failures == 0 ? 0 : 1;
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public sealed class ExecutionProbe
{
    public TaskCompletionSource<Guid> Executed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int ExecutionCount;
}

public sealed class SmokeJobs(ExecutionProbe probe)
{
    [TickerFunction("LicenseSmoke")]
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref probe.ExecutionCount);
        probe.Executed.TrySetResult(context.Id);
        return Task.CompletedTask;
    }
}
