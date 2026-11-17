using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace DashboardTestApp;

/// <summary>
/// Real-life situations the two promises catch, one POST each, so they can be watched on the Promises page.
/// Orders are fixed by hand (no AutoFix); invoices that were never scheduled are fixed automatically, five per check.
/// </summary>
public static class PromiseScenarios
{
    public static void MapPromiseScenarios(this WebApplication app)
    {
        var demo = app.MapGroup("/demo");

        // The app saved a paid order, then crashed before scheduling ProcessOrderJob.
        // Expect: "Never scheduled" about a minute later, fixed with Run fix.
        demo.MapPost("/order-not-scheduled", async (BillingDbContext db) =>
            Results.Ok(await NewOrderAsync(db, "Initrode")));

        // A job was scheduled, then someone cancelled it before it ran.
        // Expect: "Never ran" (cancelled) about a minute later; Run fix schedules a new job.
        demo.MapPost("/order-cancelled", async (BillingDbContext db, ITimeTickerManager<TimeTickerEntity> jobs) =>
        {
            var order = await NewOrderAsync(db, "Vandelay Industries");
            var job = await ScheduleOrderAsync(jobs, order, DateTime.UtcNow.AddMinutes(10));
            var cancelled = await jobs.CancelAsync(job);
            return Results.Ok(new { order.Number, job, cancelled = cancelled.IsSucceeded });
        });

        // The scheduler is down while an order's job comes due.
        // Expect: "Never ran" (still waiting) once it is a minute past due; Run waiting job, or /demo/scheduler-start, runs it.
        demo.MapPost("/scheduler-outage", async (BillingDbContext db, ITimeTickerManager<TimeTickerEntity> jobs, ITickerQHostScheduler scheduler) =>
        {
            await scheduler.StopAsync();
            var order = await NewOrderAsync(db, "Kramerica");
            var job = await ScheduleOrderAsync(jobs, order, DateTime.UtcNow);
            return Results.Ok(new { order.Number, job, schedulerRunning = scheduler.IsRunning });
        });
        demo.MapPost("/scheduler-start", async (ITickerQHostScheduler scheduler) =>
        {
            await scheduler.StartAsync();
            return Results.Ok(new { schedulerRunning = scheduler.IsRunning });
        });

        // SendInvoice emails the customer but a bug loses the status update.
        // Expect: "Job ran, row not updated" — report only, no fix button, because running it again would email twice.
        demo.MapPost("/invoice-lost-update", async (BillingDbContext db, ITimeTickerManager<TimeTickerEntity> jobs) =>
        {
            var invoice = await NewInvoiceAsync(db, "Pied Piper", "ap@piedpiper.test", lostUpdate: true);
            return Results.Ok(new { invoice.Number, job = await ScheduleInvoiceAsync(jobs, invoice) });
        });

        // The customer's email address is wrong, so SendInvoice fails.
        // Expect: counted as failed and left to Executions — not listed as a finding.
        demo.MapPost("/invoice-bad-email", async (BillingDbContext db, ITimeTickerManager<TimeTickerEntity> jobs) =>
        {
            var invoice = await NewInvoiceAsync(db, "Hooli", "not-an-email");
            return Results.Ok(new { invoice.Number, job = await ScheduleInvoiceAsync(jobs, invoice) });
        });

        // One invoice finalized without scheduling. Expect: fixed automatically on the next check.
        demo.MapPost("/invoice-not-scheduled", async (BillingDbContext db) =>
            Results.Ok((await NewInvoiceAsync(db, "Dunder Mifflin", "ap@dundermifflin.test")).Number));

        // A retry bug in the app scheduled SendInvoice twice, so the customer got two emails.
        // Expect: "Ran twice" — report only, since the emails are already out.
        demo.MapPost("/invoice-sent-twice", async (BillingDbContext db, ITimeTickerManager<TimeTickerEntity> jobs) =>
        {
            var invoice = await NewInvoiceAsync(db, "Globex", "ap@globex.test");
            return Results.Ok(new { invoice.Number, jobs = new[] { await ScheduleInvoiceAsync(jobs, invoice), await ScheduleInvoiceAsync(jobs, invoice) } });
        });

        // A double click scheduled ProcessOrderJob twice: one ran, the other waits for later.
        // Expect: "Will run twice" once the first ran; Cancel extra job cancels the waiting copy before it runs.
        demo.MapPost("/order-double-scheduled", async (BillingDbContext db, ITimeTickerManager<TimeTickerEntity> jobs) =>
        {
            var order = await NewOrderAsync(db, "Soylent");
            var first = await ScheduleOrderAsync(jobs, order, DateTime.UtcNow);
            var extra = await ScheduleOrderAsync(jobs, order, DateTime.UtcNow.AddMinutes(10));
            return Results.Ok(new { order.Number, first, extra });
        });

        // A deploy broke scheduling and many invoices were finalized without jobs at once.
        // Expect: more than the auto-fix limit of 5, so automatic fixing pauses and asks a person to look.
        demo.MapPost("/invoice-burst", async (BillingDbContext db, int? count) =>
        {
            var numbers = new List<string>();
            for (var i = 0; i < Math.Clamp(count ?? 7, 1, 50); i++)
                numbers.Add((await NewInvoiceAsync(db, $"Burst Co {i + 1}", $"ap{i + 1}@burst.test")).Number);
            return Results.Ok(numbers);
        });
    }

    private static async Task<Order> NewOrderAsync(BillingDbContext db, string customer)
    {
        var order = new Order
        {
            Number = $"ORD-{2000 + await db.Orders.CountAsync() + 1}",
            Customer = customer,
            Email = $"orders@{customer.Replace(" ", "").ToLowerInvariant()}.test",
            Amount = 120,
            Status = "Paid",
            PaidAt = DateTime.UtcNow,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<Invoice> NewInvoiceAsync(BillingDbContext db, string customer, string email, bool lostUpdate = false)
    {
        var invoice = new Invoice
        {
            Number = $"INV-{1000 + await db.Invoices.CountAsync() + 1}",
            Customer = customer,
            Email = email,
            Amount = 450,
            Status = "Finalized",
            FinalizedAt = DateTime.UtcNow,
            SimulateLostUpdate = lostUpdate,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private static async Task<Guid> ScheduleOrderAsync(ITimeTickerManager<TimeTickerEntity> jobs, Order order, DateTime at)
    {
        var request = new ProcessOrderRequest
        {
            OrderId = order.Number,
            Customer = new CustomerInfo { Email = order.Email, DisplayName = order.Customer },
            Amount = order.Amount,
        };
        var result = (await jobs.AddAsync(new TimeTickerEntity
        {
            Function = "ProcessOrderJob",
            Description = $"Process {order.Number}",
            ExecutionTime = at,
            Request = TickerHelper.CreateTickerRequest(request, "ProcessOrderJob"),
        })).EnsureSuccess();
        return result.Result.Id;
    }

    private static async Task<Guid> ScheduleInvoiceAsync(ITimeTickerManager<TimeTickerEntity> jobs, Invoice invoice)
    {
        var result = (await jobs.AddAsync(new TimeTickerEntity
        {
            Function = "SendInvoice",
            Description = $"Send {invoice.Number}",
            ExecutionTime = DateTime.UtcNow,
            Request = TickerHelper.CreateTickerRequest(new SendInvoiceRequest { InvoiceId = invoice.Id }, "SendInvoice"),
        })).EnsureSuccess();
        return result.Result.Id;
    }
}
