using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

var builder = WebApplication.CreateBuilder(args);

// ── Assistant chat history ──
// Per-user, server-side chat history (the SPA shows the conversation list
// automatically when a store is registered). Two modes:
//   ASSISTANT_HISTORY=db      → packaged EF store: efOptions.AddAssistantHistory()
//                               inside AddOperationalStore below (default)
//   ASSISTANT_HISTORY=openai  → sample store: only a pointer row locally, the
//                               transcript held at OpenAI (Conversations API)
var historyMode = Environment.GetEnvironmentVariable("ASSISTANT_HISTORY") ?? "db";
var useOpenAIHistory = string.Equals(historyMode, "openai", StringComparison.OrdinalIgnoreCase);
if (useOpenAIHistory)
{
    builder.Services.AddDbContext<DashboardTestApp.AssistantPointerDbContext>(o =>
        o.UseSqlite("Data Source=dashboard-assistant.db"));
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<TickerQ.Utilities.Interfaces.IAssistantHistoryStore, DashboardTestApp.OpenAIConversationHistoryStore>();
}

// Test harness for the new React dashboard. File-based SQLite; delete
// dashboard-testapp.db for a clean slate (the time-ticker seeder below re-runs
// every startup, so a fresh DB avoids duplicate one-off tickers).
builder.Services.AddTickerQ(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
            dbOptions.UseSqlite("Data Source=dashboard-testapp.db"));

        // Startup migration hook (no-op here since this sample has no migrations —
        // EnsureCreated below builds the schema — but proves the wiring fires).
        efOptions.AutoMigrateDatabase();

        // Packaged assistant history (db mode): maps AssistantConversations +
        // AssistantMessages into the TickerQ model and registers the EF store.
        if (!useOpenAIHistory)
            efOptions.AddAssistantHistory(h => h.MaxConversationsPerUser = 50);
    });

    // Seed a couple of one-off time tickers on startup. AddOnceAsync makes the
    // seeder idempotent (keyed by init identifier) — restarting the app no longer
    // duplicates them. Cron tickers are seeded automatically from the
    // [TickerFunction] cron expressions on SampleJobs below.
    options.UseTickerSeeder(async timeManager =>
    {
        await timeManager.AddOnceAsync("seed:welcome-email", new TimeTickerEntity
        {
            Function = "SendWelcomeEmail",
            Description = "One-off welcome email",
            ExecutionTime = DateTime.UtcNow.AddSeconds(20),
        });

        await timeManager.AddOnceAsync("seed:invoice", new TimeTickerEntity
        {
            Function = "GenerateInvoice",
            Description = "Scheduled invoice generation",
            ExecutionTime = DateTime.UtcNow.AddMinutes(2),
            Retries = 4,
            // [5, 10, 20, 40] — no more hand-rolled arrays.
            RetryIntervals = RetryPolicy.Exponential(baseSeconds: 5, retries: 4),
        });
    });

    // Failure notifications: POSTs a JSON event for terminal failures, execution
    // timeouts, and stale recoveries. Point at any HTTP sink (here: a local test
    // listener; in production a Slack webhook proxy, PagerDuty, etc.).
    options.NotifyFailuresViaWebhook("http://localhost:9911/tickerq-hook");

    // Default mount point: /tickerq/dashboard with three coexisting schemes.
    // Browser users get an HTTP-only cookie via /login; script clients can
    // use the JWT bearer token; legacy tooling can use Basic auth. On a 401
    // the response advertises every applicable challenge — each client
    // picks the one it understands.
    // Plain HTTP for local dev → SecureCookie=false so the browser stores it.
    options.AddDashboard(dash =>
    {
        // Branding + defaults (new in this release). The logo resolves
        // relative to the dashboard base path — favicon.svg ships in dist,
        // so this works offline.
        dash.SetTitle("Acme Jobs — Local Test");
        dash.SetLogoUrl("favicon.svg");
        dash.SetTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane"));

        dash.WithAuthentication(auth =>
        {
            auth.AddCookieLogin(c =>
            {
                c.AddUser("admin", "admin");
                c.SecureCookie = false;
            });
            auth.AddJwtBearer(j => j.AddUser("admin", "admin"));
            auth.AddBasic("admin", "admin", b => b.ChallengeBrowserPrompt = true);
        });

        // AI assistant — enabled ONLY when a provider key is present. With no
        // key, AddAssistant is never called: the Chat AI page shows its
        // "not configured" notice and the endpoint isn't mapped. Keys stay
        // server-side, supplied via environment variables:
        //   export OPENAI_API_KEY=sk-...          # OpenAI (model: OPENAI_MODEL, default gpt-4o-mini)
        //   export ANTHROPIC_API_KEY=sk-ant-...   # Anthropic (claude-opus-4-8)
        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(openAiApiKey))
        {
            var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
            dash.AddAssistant(a => a
                .UseChatClient(new OpenAI.OpenAIClient(openAiApiKey)
                    .GetChatClient(openAiModel)
                    .AsIChatClient())
                .WithModelName(openAiModel));
        }
        else if (!string.IsNullOrEmpty(anthropicApiKey))
        {
            dash.AddAssistant(a => a
                .UseChatClient(new DashboardTestApp.AnthropicChatClient(anthropicApiKey, "claude-opus-4-8"))
                .WithModelName("claude-opus-4-8"));
        }
    });
});

var app = builder.Build();

// EnsureCreated builds the operational-store schema straight from the model,
// so this throwaway app needs no migrations of its own.
using (var scope = app.Services.CreateScope())
{
    // In db history mode the assistant tables are part of the TickerQ model,
    // so this single EnsureCreated builds them too.
    var db = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    db.Database.EnsureCreated();

    if (useOpenAIHistory)
    {
        var pointerDb = scope.ServiceProvider.GetRequiredService<DashboardTestApp.AssistantPointerDbContext>();
        pointerDb.Database.EnsureCreated();
    }
}

app.UseTickerQ();

// On-demand scheduling so you can watch executions appear live in the dashboard.
app.MapPost("/schedule/{function}", async (
    string function,
    ITimeTickerManager<TimeTickerEntity> manager,
    int? inSeconds) =>
{
    var result = await manager.AddAsync(new TimeTickerEntity
    {
        Function = function,
        ExecutionTime = DateTime.UtcNow.AddSeconds(inSeconds ?? 5),
    });

    return Results.Ok(new { result.Result.Id, result.Result.Function, result.Result.ExecutionTime });
});

app.MapGet("/", () => Results.Redirect("/tickerq/dashboard"));

app.Run("http://localhost:5210");

// Sample jobs — each [TickerFunction] name shows up on the dashboard's Functions
// view. The four with a cron expression are auto-seeded as cron tickers at startup.
public class SampleJobs
{
    private readonly ILogger<SampleJobs> _logger;

    public SampleJobs(ILogger<SampleJobs> logger) => _logger = logger;

    [TickerFunction("Heartbeat", "* * * * *")]
    public Task Heartbeat(TickerFunctionContext context, CancellationToken ct)
    {
        // ILogger on purpose (not Console) — exercises the in-process log capture
        // that feeds the dashboard's per-execution Logs panel.
        _logger.LogInformation("Heartbeat tick at {Now:O} (occurrence {Id})", DateTime.UtcNow, context.Id);
        _logger.LogWarning("Heartbeat sample warning line");
        return Task.CompletedTask;
    }

    [TickerFunction("CleanupTempFiles", "*/5 * * * *")]
    public async Task CleanupTempFiles(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("Scanning temp directories…");
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
        _logger.LogInformation("Cleaned temp files at {Now:O}", DateTime.UtcNow);
    }

    [TickerFunction("FlakyJob", "*/2 * * * *")]
    public Task FlakyJob(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("FlakyJob attempt starting…");
        if (Random.Shared.Next(2) == 0)
        {
            _logger.LogError("FlakyJob is about to fail (random failure for retry testing)");
            throw new InvalidOperationException("FlakyJob failed on purpose (random failure for retry testing).");
        }

        _logger.LogInformation("FlakyJob succeeded at {Now:O}", DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [TickerFunction("DailyReport", "0 0 * * *")]
    public async Task DailyReport(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("Generating daily report…");
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        _logger.LogInformation("Daily report generated at {Now:O}", DateTime.UtcNow);
    }

    // Fails every run → guaranteed Failed rows for testing the Executions
    // page's new Retry button (single retry re-runs the parent cron on demand).
    [TickerFunction("AlwaysFails", "* * * * *")]
    public Task AlwaysFails(TickerFunctionContext context, CancellationToken ct)
    {
        throw new InvalidOperationException("AlwaysFails failed on purpose — use the Retry button on the Executions page.");
    }

    // Runs for 3 minutes → stays InProgress long enough to test the Cancel
    // action in the detail panels. Schedule via: curl -X POST localhost:5210/schedule/LongRunning
    [TickerFunction("LongRunning")]
    public async Task LongRunning(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("LongRunning started at {Now:O}", DateTime.UtcNow);
        for (var i = 1; i <= 6; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            _logger.LogInformation("LongRunning progress: {Done}/6 slices done", i);
        }
        _logger.LogInformation("LongRunning finished at {Now:O}", DateTime.UtcNow);
    }

    [TickerFunction("SendWelcomeEmail")]
    public Task SendWelcomeEmail(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("Welcome email sent at {Now:O}", DateTime.UtcNow);
        return Task.CompletedTask;
    }

    // Honors its CancellationToken and finishes in ~15s → exercises the graceful
    // shutdown drain (SIGTERM mid-run should let it complete) and cooperative
    // execution timeouts (set TimeoutSeconds < 15 and it lands Cancelled).
    [TickerFunction("SlowFinish")]
    public async Task SlowFinish(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("SlowFinish started — 15s of honest work ahead");
        await Task.Delay(TimeSpan.FromSeconds(15), ct);
        _logger.LogInformation("SlowFinish completed at {Now:O}", DateTime.UtcNow);
    }

    // Deliberately IGNORES its CancellationToken → exercises the non-cooperative
    // timeout path (abandoned after timeout + grace, Cancelled with reason).
    [TickerFunction("StubbornJob")]
    public async Task StubbornJob(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("StubbornJob started — ignoring cancellation for 2 minutes");
        await Task.Delay(TimeSpan.FromMinutes(2), CancellationToken.None);
        _logger.LogInformation("StubbornJob finished (nobody stopped me)");
    }

    [TickerFunction("GenerateInvoice")]
    public async Task GenerateInvoice(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("Generating invoice…");
        await Task.Delay(TimeSpan.FromMilliseconds(750), ct);
        _logger.LogInformation("Invoice generated at {Now:O}", DateTime.UtcNow);
    }
}

