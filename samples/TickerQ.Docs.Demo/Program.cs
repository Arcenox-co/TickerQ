using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Docs.Demo;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

Console.WriteLine("[docs-demo] creating host builder");
var builder = WebApplication.CreateSlimBuilder(args);
Console.WriteLine("[docs-demo] host builder ready");

var port = int.TryParse(Environment.GetEnvironmentVariable("DOCS_DEMO_PORT"), out var configuredPort)
    ? configuredPort
    : 5290;
var nodeName = Environment.GetEnvironmentVariable("DOCS_DEMO_NODE") ?? "docs-eu-1";
var databasePath = Environment.GetEnvironmentVariable("DOCS_DEMO_DB")
    ?? Path.Combine(Path.GetTempPath(), "tickerq-docs-demo", "tickerq-docs-demo.db");
var keysPath = Environment.GetEnvironmentVariable("DOCS_DEMO_KEYS")
    ?? Path.Combine(Path.GetTempPath(), "tickerq-docs-demo", "data-protection");
var sourcePath = Environment.GetEnvironmentVariable("DOCS_DEMO_SOURCE")
    ?? builder.Environment.ContentRootPath;
var licensePath = Environment.GetEnvironmentVariable("TICKERQ_LICENSE_PATH");

Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
Directory.CreateDirectory(keysPath);

builder.Services.AddDataProtection()
    .SetApplicationName("tickerq-docs-demo")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

Console.WriteLine("[docs-demo] configuring TickerQ");
builder.Services.AddTickerQ(options =>
{
    if (!string.IsNullOrWhiteSpace(licensePath))
        options.UseLicense(licensePath);

    options.UseApplicationNamespace("tickerq-docs-demo");
    options.UseReconciliationEpoch(1);
    options.EnableDetailedLogging();
    options.AddScheduleDateProvider<DocsDemoHolidayProvider>();
    options.UseSchedulePolicySeeder(async (policies, cancellationToken) =>
    {
        await policies.UpsertAsync(SchedulePolicy.Create("Orders operating calendar")
            .WithDescription("Every day except the annual New Year closure")
            .InTimeZone("Europe/Tirane")
            .ExcludeAnnualDate(1, 1)
            .ExcludeDatesFrom("Albanian public holidays")
            .Build(), cancellationToken);
        await policies.UpsertAsync(SchedulePolicy.Create("Business hours")
            .WithDescription("Weekday office hours for customer-facing work")
            .InTimeZone("Europe/Tirane")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday)
            .AllowBetween(new TimeOnly(8, 0), new TimeOnly(18, 0))
            .ExcludeAnnualDate(12, 25)
            .Build(), cancellationToken);
        await policies.UpsertAsync(SchedulePolicy.Create("Maintenance window")
            .WithDescription("Sunday night database maintenance")
            .InTimeZone("UTC")
            .AllowWeekdays(DayOfWeek.Sunday)
            .AllowBetween(new TimeOnly(1, 0), new TimeOnly(4, 0))
            .Build(), cancellationToken);
        await policies.UpsertAsync(SchedulePolicy.Create("Payments settlement")
            .WithDescription("Settle payments during business hours, never during maintenance or bank holidays")
            .InTimeZone("Europe/Tirane")
            .AllowBetween(new TimeOnly(6, 0), new TimeOnly(22, 0))
            .ExcludeDatesFrom("Albanian public holidays")
            .Require("Business hours")
            .ExceptWhen("Maintenance window")
            .Build(), cancellationToken);
    });

    options.ConfigureScheduler(scheduler =>
    {
        scheduler.NodeIdentifier = nodeName;
        scheduler.NodeDisplayName = nodeName == "docs-eu-1" ? "Orders EU · Primary" : "Orders EU · Replica";
        scheduler.AdvertisedDashboardUrl = $"http://127.0.0.1:{port}/tickerq/dashboard";
        scheduler.MaxConcurrency = 6;
        scheduler.MaxActiveWorkflows = 64;
        scheduler.MaxQueuedExecutions = 256;
        scheduler.MaxConcurrentChainSteps = 8;
        scheduler.AcquisitionBatchSize = 100;
        scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(250);
        scheduler.DefaultExecutionTimeout = TimeSpan.FromMinutes(5);
    });

    options.ConfigureExecutionLogs(logs =>
    {
        logs.PersistToDatabase = true;
        logs.MinimumLevel = LogLevel.Information;
        logs.MaxLinesPerExecution = 200;
        logs.Retention = TimeSpan.FromDays(14);
        logs.MaxStoredExecutions = 10_000;
    });

    options.ConfigureJobRetention(retention =>
    {
        retention.DeleteSucceededAfter = TimeSpan.FromDays(7);
        retention.DeleteFailedAfter = TimeSpan.FromDays(30);
        retention.DeleteCancelledAfter = TimeSpan.FromDays(14);
        retention.DeleteSkippedAfter = TimeSpan.FromDays(7);
        retention.SweepInterval = TimeSpan.FromHours(1);
        retention.BatchSize = 500;
    });

    options.AddOperationalStore(store =>
    {
        store.UseTickerQDbContext<TickerQDbContext>(db =>
            db.UseSqlite($"Data Source={databasePath}", sqlite =>
                sqlite.MigrationsAssembly(typeof(Program).Assembly.FullName)));
        store.AddAssistantHistory(history =>
        {
            history.MaxConversationsPerUser = 50;
            history.MaxMessagesPerConversation = 80;
            history.MaxInvestigationsPerNamespace = 500;
            history.InvestigationRetention = TimeSpan.FromDays(30);
            history.ReadInvestigationRetention = TimeSpan.FromDays(14);
            history.ResolvedInvestigationRetention = TimeSpan.FromDays(7);
        });
    });

    options.AddIntegrations(integrations =>
    {
        integrations.ApplicationName = "Orders API";
        integrations.EnvironmentName = "Development";
        integrations.DashboardUrl = $"http://127.0.0.1:{port}/tickerq/dashboard";
        integrations.MonitoringInterval = TimeSpan.FromSeconds(10);
    });

    options.AddDashboard(dashboard =>
    {
        dashboard.SetTitle("TickerQ · Orders API");
        dashboard.SetEnvironmentName("Development");
        dashboard.SetTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane"));
        dashboard.WithAuthentication(auth => auth.AddCookieLogin(cookie =>
        {
            cookie.AddUser("admin", "admin");
            cookie.SecureCookie = false;
            cookie.AllowEphemeralSigningKey = true;
        }));
        dashboard.AllowIntegrationManagement(_ => true);
        // DOCS_DEMO_ASSISTANT=off shows the dashboard without a connected model.
        if (Environment.GetEnvironmentVariable("DOCS_DEMO_ASSISTANT") != "off")
        dashboard.AddAssistant(assistant => assistant
            .UseChatClient(new DocsDemoChatClient())
            .WithModelName("Local documentation model")
            .ConfigureRunInvestigator(investigator => investigator
                .UseLocalSourceContext(sourcePath, source => source
                    .WithRepository("TickerQ documentation demo")
                    .WithRevision("local-current-source")
                    .WithContextLines(before: 8, after: 8)
                    .MapFunction("DocsDemo.Payment.Charge", "Program.cs"))
                .EnableAutomaticInvestigations(automatic => automatic
                    .Every(TimeSpan.FromSeconds(10))
                    .WithLookback(TimeSpan.FromHours(1))
                    .WithBatchSize(10)
                    .WithHourlyLimit(20)
                    .IncludeCancellations())));
    });
});
Console.WriteLine("[docs-demo] TickerQ configured");

Console.WriteLine("[docs-demo] building host");
var app = builder.Build();
Console.WriteLine("[docs-demo] host built; applying migrations");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    db.Database.Migrate();
}
Console.WriteLine("[docs-demo] database migrated");

app.UseTickerQ();
Console.WriteLine($"[docs-demo] dashboard ready on http://127.0.0.1:{port}/tickerq/dashboard");

app.MapPost("/demo/seed", async (
    ITimeTickerManager<TimeTickerEntity> timeManager,
    ICronTickerManager<CronTickerEntity> cronManager,
    CancellationToken cancellationToken) =>
{
    var now = DateTime.UtcNow;
    var created = new List<object>();

    var waiting = await timeManager.AddOnceAsync("docs-demo:waiting-invoice", new TimeTickerEntity
    {
        Function = "DocsDemo.Invoice.Generate",
        Description = "Generate the monthly customer invoice",
        ExecutionTime = now.AddHours(2),
        Retries = 2,
        RetryIntervals = [10, 30],
    }, cancellationToken);
    if (waiting.IsSucceeded) created.Add(new { Kind = "waiting", waiting.Result.Id });

    var progress = await timeManager.AddAsync(new TimeTickerEntity
    {
        Function = "DocsDemo.Export.Progress",
        Description = "Export 25,000 customer records",
        ExecutionTime = now.AddSeconds(3),
    }, cancellationToken);
    if (progress.IsSucceeded) created.Add(new { Kind = "progress", progress.Result.Id });

    var payload = new DemoOrderRequest(
        $"ORD-{now:HHmmss}", "customer@example.com", 249.95m, "EUR");
    var payloadChain = await timeManager.AddChainAsync(chain => chain
        .WithDescription("Order fulfilment with typed input and committed results")
        .RunAt(now.AddSeconds(2))
        .WhenMissed(MissedExecutionPolicy.SkipAfter(TimeSpan.FromMinutes(2)))
        .StartWith("DocsDemo.Order.Prepare", payload,
            step => step.Description = "Validate and prepare the order")
        .Then("DocsDemo.Invoice.Create", new DemoInvoiceRequest("Order checkout", false),
            step => step.Description = "Create invoice from the prepared order")
        .Then("DocsDemo.Receipt.Send",
            step => step.Description = "Send receipt from the committed invoice result"),
        cancellationToken);
    if (payloadChain.IsSucceeded) created.Add(new { Kind = "payload-chain", payloadChain.Result.Id });

    var grouped = await timeManager.AddChainAsync(chain =>
    {
        var group = chain
            .WithDescription("Payment workflow with parallel work and aggregate outcomes")
            .RunAt(now.AddSeconds(2))
            .StartWith("DocsDemo.Order.Validate", step => step.Description = "Validate checkout")
            .ThenMany(branches =>
            {
                branches.Add("DocsDemo.Inventory.Reserve", step => step.Description = "Reserve inventory")
                    .Then("DocsDemo.Inventory.Audit", step => step.Description = "Write inventory audit");
                branches.Add("DocsDemo.Payment.Charge", step =>
                {
                    step.Description = "Charge payment (intentional demo failure)";
                    step.Retries = 1;
                    step.RetryIntervals = [2];
                });
            });

        group.ThenAfterAll("DocsDemo.Order.Complete", step => step.Description = "Complete successful checkout");
        group.ThenAfterAnyFailed("DocsDemo.Order.HandleFailure", step => step.Description = "Handle failed checkout");
        group.ThenAfterAnyFailedOrCancelled("DocsDemo.Order.Compensate", step => step.Description = "Compensate failed work");
        group.ThenAfterAllCompleted("DocsDemo.Order.Close", step => step.Description = "Close after every branch settles");
    }, cancellationToken);
    if (grouped.IsSucceeded) created.Add(new { Kind = "grouped-chain", grouped.Result.Id });

    var recurring = await cronManager.AddChainAsync(chain => chain
        .WithExpression("*/30 * * * * *")
        .WithDescription("Refresh order projections every 30 seconds")
        .InTimeZone("Europe/Tirane")
        .UsingPolicy("Orders operating calendar")
        .WhenMissed(MissedExecutionPolicy.SkipAfter(TimeSpan.FromMinutes(2)))
        .CatchUp(CronCatchUpMode.RunLatest)
        .SkipIfAlreadyRunning()
        .StartWith("DocsDemo.Projection.Load", step => step.Description = "Load changed orders")
        .ThenMany(branches =>
        {
            branches.Add("DocsDemo.Projection.Search", step => step.Description = "Refresh search index");
            branches.Add("DocsDemo.Projection.Metrics", step => step.Description = "Refresh business metrics");
        }), cancellationToken);
    if (recurring.IsSucceeded) created.Add(new { Kind = "recurring-chain", recurring.Result.Id });

    var dailyReport = await cronManager.AddAsync(new CronTickerEntity
    {
        Function = "DocsDemo.Report.Daily",
        Expression = "0 0 9 * * *",
        Description = "Email the daily orders report",
        TimeZoneId = "Europe/Tirane",
        SchedulePolicyName = "Business hours",
        Retries = 2,
    }, cancellationToken);
    if (dailyReport.IsSucceeded) created.Add(new { Kind = "policy-cron", dailyReport.Result.Id });

    var settlement = await cronManager.AddAsync(new CronTickerEntity
    {
        Function = "DocsDemo.Payment.Settle",
        Expression = "0 */15 * * * *",
        Description = "Settle captured card payments",
        TimeZoneId = "Europe/Tirane",
        SchedulePolicyName = "Payments settlement",
        Retries = 3,
    }, cancellationToken);
    if (settlement.IsSucceeded) created.Add(new { Kind = "composed-policy-cron", settlement.Result.Id });

    var cacheWarm = await cronManager.AddAsync(new CronTickerEntity
    {
        Function = "DocsDemo.Cache.Warm",
        Expression = "0 0 */6 * * *",
        Description = "Warm the product catalogue cache (paused during migration)",
        IsEnabled = false,
    }, cancellationToken);
    if (cacheWarm.IsSucceeded) created.Add(new { Kind = "disabled-cron", cacheWarm.Result.Id });

    // A one-time job requested outside business hours moves to the next allowed instant.
    var digest = await timeManager.AddAsync(new TimeTickerEntity
    {
        Function = "DocsDemo.Report.Daily",
        Description = "Send the weekly customer digest",
        ExecutionTime = NextSaturdayNoon(now),
        SchedulePolicyName = "Business hours",
        BlockedOccurrenceBehavior = BlockedOccurrenceBehavior.MoveForward,
    }, cancellationToken);
    if (digest.IsSucceeded) created.Add(new { Kind = "moved-by-policy", digest.Result.Id });

    var failure = await timeManager.AddAsync(new TimeTickerEntity
    {
        Function = "DocsDemo.Payment.Charge",
        Description = "Intentional application failure for AI investigation",
        ExecutionTime = now.AddSeconds(1),
        Retries = 1,
        RetryIntervals = [2],
    }, cancellationToken);
    if (failure.IsSucceeded) created.Add(new { Kind = "failure", failure.Result.Id });

    return Results.Ok(new { Created = created, Dashboard = "/tickerq/dashboard" });
});

app.MapGet("/", () => Results.Redirect("/tickerq/dashboard"));

static DateTime NextSaturdayNoon(DateTime utcNow)
{
    var days = ((int)DayOfWeek.Saturday - (int)utcNow.DayOfWeek + 7) % 7;
    var saturday = utcNow.Date.AddDays(days == 0 ? 7 : days);
    return DateTime.SpecifyKind(saturday.AddHours(10), DateTimeKind.Utc);
}
app.Run($"http://127.0.0.1:{port}");

public sealed class DocsDemoJobs(ILogger<DocsDemoJobs> logger)
{
    [TickerFunction("DocsDemo.Invoice.Generate")]
    public Task GenerateInvoice(CancellationToken cancellationToken) => Task.CompletedTask;

    [TickerFunction("DocsDemo.Export.Progress")]
    public async Task ExportProgress(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        for (var percent = 0; percent <= 100; percent += 10)
        {
            logger.LogInformation("Export progress {Percent}% for {TickerId}", percent, context.Id);
            await context.ReportProgressAsync(percent, $"Exporting customer batch {percent / 10 + 1} of 11", cancellationToken);
            if (percent < 100) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    [TickerFunction("DocsDemo.Order.Prepare", ResultType = typeof(DemoPreparedOrderResult))]
    public async Task<DemoPreparedOrderResult> PrepareOrder(
        TickerFunctionContext<DemoOrderRequest> context,
        CancellationToken cancellationToken)
    {
        await context.ReportProgressAsync(40, "Validating order", cancellationToken);
        await Task.Delay(700, cancellationToken);
        await context.ReportProgressAsync(100, "Order prepared", cancellationToken);
        return new DemoPreparedOrderResult(
            context.Request.OrderId,
            context.Request.CustomerEmail,
            context.Request.Amount,
            context.Request.Currency,
            DateTime.UtcNow);
    }

    [TickerFunction("DocsDemo.Invoice.Create", ResultType = typeof(DemoInvoiceResult))]
    public async Task<DemoInvoiceResult> CreateInvoice(
        TickerFunctionContext<DemoInvoiceRequest> context,
        CancellationToken cancellationToken)
    {
        var parent = context.GetParentResult<DemoPreparedOrderResult>();
        await context.ReportProgressAsync(100, "Invoice created", cancellationToken);
        return new DemoInvoiceResult(
            $"INV-{parent.OrderId}", parent.OrderId, parent.Amount, parent.Currency,
            context.Request.BillingReason, context.Request.SendPaperCopy, DateTime.UtcNow);
    }

    [TickerFunction("DocsDemo.Receipt.Send", ResultType = typeof(DemoReceiptResult))]
    public async Task<DemoReceiptResult> SendReceipt(
        TickerFunctionContext context,
        CancellationToken cancellationToken)
    {
        var invoice = context.GetParentResult<DemoInvoiceResult>();
        await context.ReportProgressAsync(100, "Receipt delivered", cancellationToken);
        return new DemoReceiptResult(invoice.InvoiceId, "customer@example.com", "Delivered", DateTime.UtcNow);
    }

    [TickerFunction("DocsDemo.Order.Validate")]
    public Task Validate(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Checkout validated", cancellationToken);

    [TickerFunction("DocsDemo.Inventory.Reserve")]
    public Task Reserve(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Inventory reserved", cancellationToken);

    [TickerFunction("DocsDemo.Inventory.Audit")]
    public Task Audit(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Inventory audit written", cancellationToken);

    [TickerFunction("DocsDemo.Payment.Charge")]
    public async Task Charge(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        await context.ReportProgressAsync(55, "Authorizing payment", cancellationToken);
        logger.LogError("Payment gateway rejected the documentation demo charge for {TickerId}", context.Id);
        throw new InvalidOperationException("Payment authorization was declined by the documentation demo gateway.");
    }

    [TickerFunction("DocsDemo.Order.Complete")]
    public Task Complete(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Order completed", cancellationToken);

    [TickerFunction("DocsDemo.Order.HandleFailure")]
    public Task HandleFailure(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Failure recorded for operator review", cancellationToken);

    [TickerFunction("DocsDemo.Order.Compensate")]
    public Task Compensate(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Reserved resources released", cancellationToken);

    [TickerFunction("DocsDemo.Order.Close")]
    public Task Close(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Workflow closed", cancellationToken);

    [TickerFunction("DocsDemo.Report.Daily")]
    public Task DailyReport(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Daily report sent", cancellationToken);

    [TickerFunction("DocsDemo.Payment.Settle")]
    public Task Settle(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Payments settled", cancellationToken);

    [TickerFunction("DocsDemo.Cache.Warm")]
    public Task WarmCache(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Catalogue cache warmed", cancellationToken);

    [TickerFunction("DocsDemo.Projection.Load")]
    public Task LoadProjection(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Changes loaded", cancellationToken, 350);

    [TickerFunction("DocsDemo.Projection.Search")]
    public Task RefreshSearch(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Search index refreshed", cancellationToken, 450);

    [TickerFunction("DocsDemo.Projection.Metrics")]
    public Task RefreshMetrics(TickerFunctionContext context, CancellationToken cancellationToken)
        => RunStep(context, "Metrics refreshed", cancellationToken, 500);

    private async Task RunStep(
        TickerFunctionContext context,
        string message,
        CancellationToken cancellationToken,
        int delayMilliseconds = 700)
    {
        await context.ReportProgressAsync(35, message, cancellationToken);
        await Task.Delay(delayMilliseconds, cancellationToken);
        await context.ReportProgressAsync(100, message, cancellationToken);
        logger.LogInformation("{Message} for {TickerId}", message, context.Id);
    }
}

public sealed class DocsDemoHolidayProvider : IScheduleDateProvider
{
    public string Name => "Albanian public holidays";

    public bool IsExcluded(DateOnly localDate) => localDate is
        { Month: 1, Day: 1 or 2 } or
        { Month: 11, Day: 28 or 29 };

    public string GetReason(DateOnly localDate) =>
        $"{localDate:yyyy-MM-dd} is a public holiday in the documentation demo.";
}

public sealed record DemoOrderRequest(string OrderId, string CustomerEmail, decimal Amount, string Currency);
public sealed record DemoPreparedOrderResult(
    string OrderId, string CustomerEmail, decimal Amount, string Currency, DateTime PreparedAtUtc);
public sealed record DemoInvoiceRequest(string BillingReason, bool SendPaperCopy);
public sealed record DemoInvoiceResult(
    string InvoiceId, string OrderId, decimal Total, string Currency,
    string BillingReason, bool SendPaperCopy, DateTime CreatedAtUtc);
public sealed record DemoReceiptResult(
    string InvoiceId, string Recipient, string DeliveryStatus, DateTime SentAtUtc);

[JsonSerializable(typeof(DemoOrderRequest))]
[JsonSerializable(typeof(DemoPreparedOrderResult))]
[JsonSerializable(typeof(DemoInvoiceRequest))]
[JsonSerializable(typeof(DemoInvoiceResult))]
[JsonSerializable(typeof(DemoReceiptResult))]
public partial class DocsDemoJsonContext : JsonSerializerContext;
