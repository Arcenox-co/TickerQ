using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace DashboardTestApp;

// The app's own data, in its own SQLite file (dashboard-billing.db), separate from TickerQ's tables.
// Promises compare it with TickerQ's jobs: delete both .db files for a clean slate.
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Invoice
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Email { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>Draft, Finalized or Sent.</summary>
    public string Status { get; set; } = "Draft";
    public DateTime? FinalizedAt { get; set; }
    public DateTime? SentAt { get; set; }
    /// <summary>Demo only: the next SendInvoice run sends the email but loses the status update.</summary>
    public bool SimulateLostUpdate { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Email { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>Pending, Paid or Processed.</summary>
    public string Status { get; set; } = "Pending";
    public DateTime? PaidAt { get; set; }
}

public sealed class SendInvoiceRequest
{
    public int InvoiceId { get; set; }
}

public class BillingJobs(BillingDbContext db, ILogger<BillingJobs> logger)
{
    [TickerFunction("SendInvoice")]
    public async Task SendInvoice(TickerFunctionContext<SendInvoiceRequest> context, CancellationToken ct)
    {
        var invoice = await db.Invoices.FindAsync([context.Request.InvoiceId], ct)
            ?? throw new InvalidOperationException($"Invoice {context.Request.InvoiceId} does not exist.");

        if (!invoice.Email.Contains('@'))
            throw new InvalidOperationException($"Cannot send {invoice.Number}: '{invoice.Email}' is not an email address.");

        logger.LogInformation("Emailed {Number} ({Amount:C}) to {Email}", invoice.Number, invoice.Amount, invoice.Email);

        if (invoice.SimulateLostUpdate)
        {
            // The bug a promise catches: the email went out, the job succeeded, but the status never changed.
            invoice.SimulateLostUpdate = false;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("{Number}: the status update was lost (simulated bug)", invoice.Number);
            return;
        }

        invoice.Status = "Sent";
        invoice.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Processes a paid order and marks it Processed, which is what the orders promise checks.</summary>
public sealed class ProcessOrderJob(BillingDbContext db, ILogger<ProcessOrderJob> logger) : ITickerFunction<ProcessOrderRequest>
{
    public async Task ExecuteAsync(TickerFunctionContext<ProcessOrderRequest> context, CancellationToken ct = default)
    {
        var order = context.Request;
        logger.LogInformation("Processing order {OrderId} for {Email} — {Amount} (priority {Priority})",
            order.OrderId, order.Customer.Email, order.Amount, order.Priority);

        var stored = await db.Orders.FirstOrDefaultAsync(o => o.Number == order.OrderId, ct);
        if (stored != null)
        {
            stored.Status = "Processed";
            await db.SaveChangesAsync(ct);
        }
    }
}

public static class Billing
{
    public const string ConnectionString = "Data Source=dashboard-billing.db";

    /// <summary>
    /// Seeds one invoice and order per case a promise reports, the first time the app starts:
    /// a job that works, no job at all, a job that fails, and a job that succeeds without updating the record.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (await db.Invoices.AnyAsync())
            return;

        var finalized = DateTime.UtcNow;
        db.Invoices.AddRange(
            new Invoice { Number = "INV-1001", Customer = "Acme", Email = "billing@acme.test", Amount = 1200, Status = "Finalized", FinalizedAt = finalized },
            new Invoice { Number = "INV-1002", Customer = "Globex", Email = "ap@globex.test", Amount = 860, Status = "Finalized", FinalizedAt = finalized },
            new Invoice { Number = "INV-1003", Customer = "Initech", Email = "initech-billing", Amount = 430, Status = "Finalized", FinalizedAt = finalized },
            new Invoice { Number = "INV-1004", Customer = "Umbrella", Email = "finance@umbrella.test", Amount = 2950, Status = "Finalized", FinalizedAt = finalized, SimulateLostUpdate = true },
            new Invoice { Number = "INV-1005", Customer = "Hooli", Email = "ap@hooli.test", Amount = 75, Status = "Draft" });
        db.Orders.AddRange(
            new Order { Number = "ORD-2001", Customer = "Acme", Email = "orders@acme.test", Amount = 310, Status = "Paid", PaidAt = finalized },
            new Order { Number = "ORD-2002", Customer = "Globex", Email = "orders@globex.test", Amount = 145, Status = "Paid", PaidAt = finalized },
            new Order { Number = "ORD-2003", Customer = "Initech", Email = "orders@initech.test", Amount = 990, Status = "Paid", PaidAt = finalized },
            new Order { Number = "ORD-2004", Customer = "Hooli", Email = "orders@hooli.test", Amount = 60, Status = "Pending" });
        await db.SaveChangesAsync();

        var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        // INV-1002 gets no job: the app "crashed" between saving the invoice and scheduling it.
        foreach (var invoice in await db.Invoices.Where(i => i.Number != "INV-1002" && i.Status == "Finalized").ToListAsync())
        {
            (await manager.AddAsync(new TimeTickerEntity
            {
                Function = "SendInvoice",
                Description = $"Send {invoice.Number}",
                ExecutionTime = DateTime.UtcNow.AddSeconds(5),
                Request = TickerQ.Utilities.TickerHelper.CreateTickerRequest(new SendInvoiceRequest { InvoiceId = invoice.Id }, "SendInvoice"),
            })).EnsureSuccess();
        }

        // Only ORD-2001 is processed; ORD-2002 and ORD-2003 are left for the orders promise to find.
        (await manager.AddAsync(new TimeTickerEntity
        {
            Function = nameof(ProcessOrderJob),
            Description = "Process ORD-2001",
            ExecutionTime = DateTime.UtcNow.AddSeconds(5),
            Request = TickerQ.Utilities.TickerHelper.CreateTickerRequest(new ProcessOrderRequest
            {
                OrderId = "ORD-2001",
                Customer = new CustomerInfo { Email = "orders@acme.test", DisplayName = "Acme" },
                Amount = 310,
            }, nameof(ProcessOrderJob)),
        })).EnsureSuccess();
    }
}
