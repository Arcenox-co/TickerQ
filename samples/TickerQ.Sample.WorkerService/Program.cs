using TickerQ.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

var builder = Host.CreateDefaultBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.UseWindowsService();
}

builder.ConfigureServices(services =>
{
    services.AddTickerQ();
    services.AddHostedService<SampleScheduler>();
});

var host = builder.Build();

// Ensure TickerFunction registrations in hosting scenarios where ModuleInitializer may be skipped
global::TickerQ.Sample.WorkerService.TickerQInstanceFactoryExtensions.Initialize();

// Initialize TickerQ for generic host applications
host.UseTickerQ();

await host.RunAsync();

public class WorkerServiceSampleJobs
{
    private readonly ILogger<WorkerServiceSampleJobs> _logger;

    public WorkerServiceSampleJobs(ILogger<WorkerServiceSampleJobs> logger)
    {
        _logger = logger;
    }

    [TickerFunction("WorkerServiceSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[WorkerService] Hello from TickerQ! Id={Id}, ScheduledFor={ScheduledFor:O}",
            context.Id,
            context.ScheduledFor);

        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup
public class SampleScheduler : IHostedService
{
    private readonly ITimeTickerManager<TimeTickerEntity> _timeTickerManager;
    private readonly ILogger<SampleScheduler> _logger;

    public SampleScheduler(
        ITimeTickerManager<TimeTickerEntity> timeTickerManager,
        ILogger<SampleScheduler> logger)
    {
        _timeTickerManager = timeTickerManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await _timeTickerManager.AddAsync(new TimeTickerEntity
        {
            Function = "WorkerServiceSample_HelloWorld",
            ExecutionTime = DateTime.UtcNow.AddSeconds(5)
        }, cancellationToken);

        if (!result.IsSucceeded)
        {
            _logger.LogError(result.Exception, "Failed to schedule worker service sample job.");
            return;
        }

        _logger.LogInformation(
            "Scheduled worker service sample job Id={Id}, ScheduledFor={ScheduledFor:O}",
            result.Result.Id,
            result.Result.ExecutionTime);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
