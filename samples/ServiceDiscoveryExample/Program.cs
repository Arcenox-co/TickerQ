using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.DependencyInjection;
using TickerQ.DependencyInjection.Hosting;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace ServiceDiscoveryExample;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("TickerQ Service Discovery Example");
        Console.WriteLine("=================================");

        var builder = Host.CreateApplicationBuilder(args);

        // Configure TickerQ with service discovery
        builder.Services.AddTickerQ(options =>
        {
            // Register assemblies for service discovery
            options.RegisterServicesFromAssemblies(
                Assembly.GetExecutingAssembly(),           // Current assembly
                typeof(ExternalService).Assembly           // External assembly (same in this case)
            );

            // You can chain multiple calls
            options.RegisterServicesFromAssemblies(typeof(Program).Assembly);

            // Set other options
            options.SetMaxConcurrency(2);
            options.SetInstanceIdentifier("ServiceDiscoveryExample");
        });

        // Register additional services
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<IDataService, DataService>();

        var host = builder.Build();

        Console.WriteLine("Starting TickerQ with service discovery...");
        
        // Start TickerQ - it will automatically discover and register functions from the specified assemblies
        host.UseTickerQ();

        Console.WriteLine("TickerQ started. Press Ctrl+C to stop.");
        
        await host.RunAsync();
    }
}

// Example services that will be discovered
public class ScheduledTaskService
{
    private readonly ILogger<ScheduledTaskService> _logger;
    private readonly IEmailService _emailService;

    public ScheduledTaskService(ILogger<ScheduledTaskService> logger, IEmailService emailService)
    {
        _logger = logger;
        _emailService = emailService;
    }

    [TickerFunction("DailyReport", "0 0 8 * * *", TickerTaskPriority.Normal)]
    public async Task GenerateDailyReport(CancellationToken cancellationToken, TickerFunctionContext context)
    {
        _logger.LogInformation("Generating daily report at {Time}", DateTime.Now);
        
        // Simulate report generation
        await Task.Delay(2000, cancellationToken);
        
        await _emailService.SendEmailAsync("admin@company.com", "Daily Report", "Report generated successfully");
        
        _logger.LogInformation("Daily report completed");
    }

    [TickerFunction("HealthCheck", "0 */5 * * * *", TickerTaskPriority.High)]
    public async Task PerformHealthCheck(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing health check at {Time}", DateTime.Now);
        
        // Simulate health check
        await Task.Delay(500, cancellationToken);
        
        _logger.LogInformation("Health check completed - System is healthy");
    }

    [TickerFunction("DataCleanup", TickerTaskPriority.LongRunning)]
    public async Task CleanupOldData(TickerFunctionContext context, IDataService dataService)
    {
        _logger.LogInformation("Starting data cleanup at {Time}", DateTime.Now);
        
        // Simulate long-running cleanup task
        await dataService.CleanupOldRecordsAsync();
        
        _logger.LogInformation("Data cleanup completed");
    }
}

public class ExternalService
{
    private readonly ILogger<ExternalService> _logger;

    public ExternalService(ILogger<ExternalService> logger)
    {
        _logger = logger;
    }

    [TickerFunction("SyncExternalData", "0 0 */2 * * *")]
    public static async Task SyncWithExternalSystem(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing with external system at {DateTime.Now}");
        
        // Simulate external API call
        await Task.Delay(1000, cancellationToken);
        
        Console.WriteLine("External sync completed");
    }

    [TickerFunction("ProcessNotifications", "0 */10 * * * *")]
    public async Task ProcessPendingNotifications(TickerFunctionContext<NotificationRequest> context)
    {
        _logger.LogInformation("Processing notification: {Message}", context.Request?.Message ?? "No message");
        
        // Simulate notification processing
        await Task.Delay(300);
        
        _logger.LogInformation("Notification processed");
    }
}

// Supporting interfaces and classes
public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        _logger.LogInformation("Sending email to {To}: {Subject}", to, subject);
        
        // Simulate email sending
        await Task.Delay(100);
        
        _logger.LogInformation("Email sent successfully");
    }
}

public interface IDataService
{
    Task CleanupOldRecordsAsync();
}

public class DataService : IDataService
{
    private readonly ILogger<DataService> _logger;

    public DataService(ILogger<DataService> logger)
    {
        _logger = logger;
    }

    public async Task CleanupOldRecordsAsync()
    {
        _logger.LogInformation("Cleaning up old records...");
        
        // Simulate database cleanup
        await Task.Delay(3000);
        
        _logger.LogInformation("Cleanup completed - 150 old records removed");
    }
}

public class NotificationRequest
{
    public string Message { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public DateTime ScheduledTime { get; set; }
}
