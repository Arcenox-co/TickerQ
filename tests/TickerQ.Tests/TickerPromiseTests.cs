using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Promises;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Events;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Promises;

namespace TickerQ.Tests;

/// <summary>
/// Promises: rows of the application's data are classified against the function's jobs, findings are fixed or
/// dismissed, scheduled checks share one lease and publish events on transitions, and declarations are validated.
/// </summary>
[Collection("TickerFunctionProviderState")]
public sealed class TickerPromiseTests : IDisposable
{
    public sealed class Job : TimeTickerEntity<Job> { }
    public sealed class Cron : CronTickerEntity { }

    public sealed class Customer
    {
        public Guid Id { get; set; }
    }

    public sealed class OrderRequest
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
        public Customer Customer { get; set; }
    }

    public sealed class OtherRequest
    {
        public string Value { get; set; }
    }

    public sealed class Order
    {
        public string Number { get; set; } = "";
        public string Status { get; set; } = "Paid";
        public DateTime? PaidAt { get; set; }
        public decimal Amount { get; set; } = 12m;
        public Guid CustomerId { get; set; } = Guid.Parse("6f1c3c3e-2a4b-4c5d-9e8f-0a1b2c3d4e5f");
        public DayOfWeek Day { get; set; }
    }

    /// <summary>The application's data, resolved from DI per check.</summary>
    public sealed class Shop
    {
        public List<Order> Orders { get; } = new();
        public bool Fail { get; set; }
        public IQueryable<Order> Query() => Fail ? throw new InvalidOperationException("database is down") : Orders.AsQueryable();
    }

    public sealed class NotRegistered
    {
        public IQueryable<Order> Orders => Enumerable.Empty<Order>().AsQueryable();
    }

    public sealed class PromiseOrderJob : ITickerFunction<OrderRequest>
    {
        public Task ExecuteAsync(TickerFunctionContext<OrderRequest> context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    public sealed class UnmappedJob : ITickerFunction<OrderRequest>
    {
        public Task ExecuteAsync(TickerFunctionContext<OrderRequest> context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CapturingSink : ITickerQEventSink
    {
        public List<TickerQEvent> Events { get; } = new();

        public ValueTask PublishAsync(TickerQEvent e, CancellationToken ct)
        {
            lock (Events) Events.Add(e);
            return default;
        }
    }

    // A provider without the promise table whose definitions column is Oracle's default NVARCHAR2(2000).
    private sealed class BoundedProvider(IServiceProvider services)
        : TickerInMemoryPersistenceProvider<Job, Cron>(services), ITickerDefinitionStore, ITickerPromiseStateStore
    {
        int? ITickerDefinitionStore.MaxDefinitionJsonLength => 2000;
        bool ITickerPromiseStateStore.HasPromiseStateTable => false;
    }

    // A store that serves no indexed delta reads, so every check reads the whole window.
    private sealed class ReloadOnlyProvider(IServiceProvider services)
        : TickerInMemoryPersistenceProvider<Job, Cron>(services), ITickerPersistenceProvider<Job, Cron>
    {
        bool ITickerPersistenceProvider<Job, Cron>.SupportsPromiseDeltaReads => false;
    }

    // A store with a change feed, like Redis: aggregates by the time they were last written, read whole.
    private sealed class FeedProvider(IServiceProvider services)
        : TickerInMemoryPersistenceProvider<Job, Cron>(services), ITickerPersistenceProvider<Job, Cron>
    {
        public int FeedReads { get; private set; }
        bool ITickerPersistenceProvider<Job, Cron>.SupportsPromiseDeltaReads => false;
        bool ITickerPersistenceProvider<Job, Cron>.SupportsPromiseChangeFeed => true;

        async Task<Job[]> ITickerPersistenceProvider<Job, Cron>.ReadTimeTickersChangedSinceAsync(string function, DateTime since, int take,
            CancellationToken cancellationToken)
        {
            FeedReads++;
            var changed = await TimeTickersQuery().Where(x => x.UpdatedAt >= since).ToArrayAsync(cancellationToken);
            return changed.Length > take ? null : changed.Where(x => x.Function == function && x.OriginCronTickerId == null).ToArray();
        }

        Task<Job[]> ITickerPersistenceProvider<Job, Cron>.ReadTimeTickerAggregatesAsync(Guid[] rootIds, CancellationToken cancellationToken)
            => TimeTickersQuery().Where(x => rootIds.Contains(x.Id)).ToArrayAsync(cancellationToken);
    }

    private const string Fn = "Promise.ProcessOrder";

    private DateTime _now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly ITickerClock _clock = Substitute.For<ITickerClock>();
    private readonly SchedulerOptionsBuilder _schedulerOptions = new();
    private readonly TickerInMemoryPersistenceProvider<Job, Cron> _provider;
    private readonly TickerManager<Job, Cron> _manager;
    private readonly ServiceProvider _services;
    private readonly Shop _shop = new();
    private readonly CapturingSink _sink = new();
    private readonly ITickerQDispatcher _dispatcher = Substitute.For<ITickerQDispatcher>();
    private readonly TickerPromiseOptions _options = new() { CheckInterval = TimeSpan.FromMinutes(1) };

    public TickerPromiseTests()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            [Fn] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0),
        });
        TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
        {
            [Fn] = (typeof(OrderRequest).FullName, typeof(OrderRequest)),
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            [Fn] = new TickerFunctionDescriptor(Fn, request: new TickerRequestContract(typeof(OrderRequest).FullName)),
        }, origin: "test");
        TickerFunctionProvider.Build();

        _clock.UtcNow.Returns(_ => _now);
        _dispatcher.IsEnabled.Returns(true);
        _schedulerOptions.BindRuntimeActivationScope("promise-" + Guid.NewGuid().ToString("N")[..8], 1, schedulerEnabled: false);
        _services = new ServiceCollection().AddSingleton(_clock).AddSingleton(_schedulerOptions).AddSingleton(_shop).BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<Job, Cron>(_services);
        _manager = new TickerManager<Job, Cron>(
            _provider, Substitute.For<ITickerQHostScheduler>(), _clock, Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext(), _dispatcher, _schedulerOptions, new TickerQActivationGate());
    }

    public void Dispose()
    {
        _services.Dispose();
        TickerFunctionProvider.Build();
    }

    // ---------- helpers ----------

    private static TickerPromiseDefinition Promise(Action<TickerPromiseBuilder<Order, OrderRequest>> configure = null, bool doneWhen = true)
        => TickerPromiseDefinition.Create<Shop, Order, OrderRequest>(Fn, null, s => s.Query(), p =>
        {
            p.Within(TimeSpan.FromMinutes(5), o => o.PaidAt)
                .Job(o => new OrderRequest
                {
                    OrderId = o.Number,
                    Amount = o.Amount,
                    Customer = o.CustomerId == Guid.Empty ? null : new Customer { Id = o.CustomerId },
                }, r => r.OrderId)
                .Label(o => $"Order {o.Number}");
            if (doneWhen)
                p.DoneWhen(o => o.Status == "Processed");
            configure?.Invoke(p);
        });

    private TickerPromiseService<Job, Cron> Service(TickerPromiseDefinition definition, TickerPromiseOptions options = null,
        JobRetentionOptions retention = null, string partition = null, ITickerQDispatcher dispatcher = null,
        TickerInMemoryPersistenceProvider<Job, Cron> provider = null)
        => new([definition], options ?? _options, retention ?? new JobRetentionOptions(), provider ?? _provider, _manager,
            dispatcher ?? _dispatcher, _services.GetRequiredService<IServiceScopeFactory>(), _clock,
            new TickerExecutionContext { RuntimePartition = partition == null ? TickerQRuntimePartition.LegacyGlobal : new TickerQRuntimePartition(partition) },
            new TickerQEventPublisher([_sink], NullLogger<TickerQEventPublisher>.Instance),
            NullLogger<TickerPromiseService<Job, Cron>>.Instance);

    private Order Paid(string number, int minutesAgo = 10, string status = "Paid")
    {
        var order = new Order { Number = number, Status = status, PaidAt = _now.AddMinutes(-minutesAgo) };
        _shop.Orders.Add(order);
        return order;
    }

    private static byte[] Request(OrderRequest request)
        => TickerHelper.CreateTickerRequest(request, TickerFunctionProvider.GetRequestTypeInfo<OrderRequest>(Fn));

    private Job NewJob(string orderId, TickerStatus status, int minutesAgo = 1)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = Fn,
            Request = Request(new OrderRequest { OrderId = orderId, Amount = 12m }),
            Status = status,
            CreatedAt = _now.AddMinutes(-minutesAgo),
            UpdatedAt = _now,
            ExecutionTime = _now.AddMinutes(-minutesAgo),
        };

    private async Task<Job> AddJob(string orderId, TickerStatus status, int minutesAgo = 1, Action<Job> configure = null)
    {
        var job = NewJob(orderId, status, minutesAgo);
        configure?.Invoke(job);
        await _provider.AddTimeTickers([job]);
        return job;
    }

    /// <summary>What the store looks like after a job changed status: the index never sees the step in between.</summary>
    private async Task SetStatus(Job job, TickerStatus status, bool finished = true)
    {
        await _provider.RemoveTimeTickers([job.Id]);
        var changed = NewJob(TickerHelper.ReadTickerRequest(job.Request, TickerFunctionProvider.GetRequestTypeInfo<OrderRequest>(Fn)).OrderId, status);
        changed.Id = job.Id;
        changed.CreatedAt = job.CreatedAt;
        changed.ExecutionTime = job.ExecutionTime;
        if (finished) changed.ExecutedAt = _now;
        await _provider.AddTimeTickers([changed]);
    }

    /// <summary>The store after a waiting job's request was edited to another row.</summary>
    private async Task EditRequest(TickerInMemoryPersistenceProvider<Job, Cron> provider, Job job, string orderId)
    {
        await provider.RemoveTimeTickers([job.Id]);
        var edited = NewJob(orderId, job.Status);
        edited.Id = job.Id;
        edited.CreatedAt = job.CreatedAt;
        edited.ExecutionTime = job.ExecutionTime;
        await provider.AddTimeTickers([edited]);
    }

    private async Task<List<Job>> JobsFor(string orderId)
    {
        var jobs = await _provider.TimeTickersQuery().Where(j => j.Function == Fn).ToArrayAsync();
        return jobs.Where(j => TickerHelper.ReadTickerRequest(j.Request, TickerFunctionProvider.GetRequestTypeInfo<OrderRequest>(Fn))?.OrderId == orderId)
            .ToList();
    }

    private static TickerPromiseFinding Finding(TickerPromiseSnapshot snapshot, string key)
        => Assert.Single(snapshot.Findings, f => f.Key == key);

    private async Task<TickerPromiseSnapshot> Single(TickerPromiseService<Job, Cron> service)
        => Assert.Single(await service.ListAsync());

    private void Advance(TimeSpan by) => _now += by;

    // ---------- classification ----------

    [Fact]
    public async Task Check_ClassifiesEveryRow_AndReportsOnlyMissedWork()
    {
        Paid("A", status: "Processed");                                   // done by its own data
        Paid("B");                                                        // never scheduled
        Paid("C"); await AddJob("C", TickerStatus.Failed);                // failed: counted only
        Paid("D"); await AddJob("D", TickerStatus.Done);                  // job ran, row not updated
        Paid("E"); await AddJob("E", TickerStatus.Idle);                  // waiting, not yet overdue
        Paid("F"); await AddJob("F", TickerStatus.InProgress);            // running
        Paid("G"); await AddJob("G", TickerStatus.Queued, 20, j => j.LockedAt = _now.AddMinutes(-1)); // being started
        Paid("H"); await AddJob("H", TickerStatus.Cancelled);             // never ran
        Paid("I"); var stuck = await AddJob("I", TickerStatus.Idle, 20);  // stuck long past due
        Paid("J"); await AddJob("J", TickerStatus.Skipped);               // never ran
        Paid("K", minutesAgo: 2);                                         // not due yet
        var service = Service(Promise());

        var snapshot = await service.CheckAsync((await Single(service)).Id);

        Assert.Null(snapshot.Error);
        Assert.Equal(10, snapshot.Counts.Watched);
        Assert.Equal(1, snapshot.Counts.Done);
        Assert.Equal(3, snapshot.Counts.Running);
        Assert.Equal(1, snapshot.Counts.Failed);
        Assert.Equal(5, snapshot.Counts.NeedsReview);
        Assert.Equal(["B", "D", "H", "I", "J"], snapshot.Findings.Select(f => f.Key).Order());
        Assert.Equal((TickerPromiseFindingKind.NoJob, TickerPromiseFix.NewJob), (Finding(snapshot, "B").Kind, Finding(snapshot, "B").Fix));
        Assert.Null(Finding(snapshot, "B").LastJob);
        Assert.Equal((TickerPromiseFindingKind.DoneButNotApplied, TickerPromiseFix.None), (Finding(snapshot, "D").Kind, Finding(snapshot, "D").Fix));
        Assert.Equal((TickerPromiseFindingKind.NeverRan, TickerPromiseFix.NewJob), (Finding(snapshot, "H").Kind, Finding(snapshot, "H").Fix));
        Assert.Contains("cancelled before it ran", Finding(snapshot, "H").Reason);
        Assert.Equal((TickerPromiseFindingKind.NeverRan, TickerPromiseFix.RunNow), (Finding(snapshot, "I").Kind, Finding(snapshot, "I").Fix));
        Assert.Equal(stuck.Id, Finding(snapshot, "I").LastJob.Id);
        Assert.Equal("The job was due 20 min ago and hasn't started.", Finding(snapshot, "I").Reason);
        Assert.Equal("No Promise.ProcessOrder job was ever scheduled for this row.", Finding(snapshot, "B").Reason);
        Assert.Equal("The job succeeded 1 min ago, but this row still doesn't meet “Status is Processed”.", Finding(snapshot, "D").Reason);
        Assert.Equal("Status is Processed", snapshot.DoneWhen);
        Assert.Equal(TickerPromiseFindingKind.NeverRan, Finding(snapshot, "J").Kind);
        Assert.Equal("Order B", Finding(snapshot, "B").Label);
        Assert.Equal("Each Order → Promise.ProcessOrder within 5 minutes after PaidAt, done when Status is Processed", snapshot.Summary);
    }

    [Fact]
    public async Task Check_WithoutDoneWhen_CountsAnySucceededJobAsDone()
    {
        Paid("A"); await AddJob("A", TickerStatus.Done, 30); await AddJob("A", TickerStatus.Failed);
        Paid("B"); await AddJob("B", TickerStatus.Failed);
        var service = Service(Promise(doneWhen: false));

        var snapshot = await service.CheckAsync((await Single(service)).Id);

        Assert.Equal((2, 1, 1, 0), (snapshot.Counts.Watched, snapshot.Counts.Done, snapshot.Counts.Failed, snapshot.Counts.NeedsReview));
        Assert.Equal("Each Order → Promise.ProcessOrder within 5 minutes after PaidAt", snapshot.Summary);
    }

    [Fact]
    public async Task Check_FindsChainSteps()
    {
        Paid("A");
        var root = NewJob("root", TickerStatus.Done);
        root.Function = "Promise.Root";
        var step = NewJob("A", TickerStatus.Done);
        root.Children.Add(step);
        await _provider.AddTimeTickers([root]);
        var service = Service(Promise(doneWhen: false));

        var snapshot = await service.CheckAsync((await Single(service)).Id);

        Assert.Equal(1, snapshot.Counts.Done);
        Assert.Empty(snapshot.Findings);
    }

    [Fact]
    public async Task MatchBy_CompositeAndNested_NormalizesValues_AndCountsEmptyParts()
    {
        var customer = Guid.Parse("6f1c3c3e-2a4b-4c5d-9e8f-0a1b2c3d4e5f");
        Paid("A").Amount = 12.0m;
        Paid("B").CustomerId = Guid.Empty; // its request has no customer: unmatchable
        // Written by another producer: an upper-case GUID and 12.00 still match 12.0.
        await _provider.AddTimeTickers([new Job
        {
            Id = Guid.NewGuid(), Function = Fn, Status = TickerStatus.Done, CreatedAt = _now.AddMinutes(-1),
            Request = Encoding.UTF8.GetBytes($$$"""{"OrderId":"other","Amount":12.00,"Customer":{"Id":"{{{customer.ToString().ToUpperInvariant()}}}"}}"""),
        }]);
        var service = Service(Promise(p => p.Job(o => new OrderRequest
        {
            OrderId = o.Number,
            Amount = o.Amount,
            Customer = o.CustomerId == Guid.Empty ? null : new Customer { Id = o.CustomerId },
        }, r => new { r.Customer.Id, r.Amount }), doneWhen: false));

        var snapshot = await service.CheckAsync((await Single(service)).Id);

        Assert.Equal(1, snapshot.Counts.Done);
        Assert.Equal(1, snapshot.Counts.Unmatchable);
        Assert.Empty(snapshot.Findings);
    }

    [Fact]
    public void Normalize_GivesEqualTextForEqualValues()
    {
        string N(object value) => TickerPromiseKey<OrderRequest>.Normalize(value);
        Assert.Equal("12", N(12.0m));
        Assert.Equal(N(12.0m), N(12));
        Assert.Equal(N(12.0m), N(12d));
        Assert.Equal("12.5", N(12.50m));
        var guid = Guid.NewGuid();
        Assert.Equal(guid.ToString("D"), N(Guid.Parse(guid.ToString().ToUpperInvariant())));
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Assert.Equal(N(utc), N(utc.ToLocalTime()));
        Assert.Equal(N(utc), N(new DateTimeOffset(utc).ToOffset(TimeSpan.FromHours(2))));
        Assert.Equal("Monday", N(DayOfWeek.Monday));
        Assert.Equal("true", N(true));
        Assert.Null(N(null));
    }

    [Fact]
    public void MatchBy_Tuple_AndSinglePart_AreRead()
    {
        var request = new OrderRequest { OrderId = "A", Amount = 3m, Customer = new Customer() };
        Assert.Equal("A · 3", TickerPromiseKey<OrderRequest>.Analyze(r => ValueTuple.Create(r.OrderId, r.Amount)).Read(request).Display);
        Assert.Equal(("A", "A", null), TickerPromiseKey<OrderRequest>.Analyze(r => r.OrderId).Read(request));
        Assert.Equal("Customer", TickerPromiseKey<OrderRequest>.Analyze(r => r.Customer).Read(new OrderRequest()).Missing);
    }

    [Fact]
    public void Summary_RendersConditionsInPlainWords()
    {
        var limit = 100m;
        System.Linq.Expressions.Expression<Func<Order, bool>> condition = o => o.Day == DayOfWeek.Friday && o.Amount > limit || !(o.Status != "Sent");
        Assert.Equal("Day is Friday and Amount is over 100 or not Status is not Sent", TickerPromiseText.Condition(condition));
        System.Linq.Expressions.Expression<Func<Order, bool>> complex = o => o.Status.StartsWith("S");
        Assert.Null(TickerPromiseText.Condition(complex));
        Assert.Equal("1 hour 30 minutes", TickerPromiseText.Duration(TimeSpan.FromMinutes(90)));
    }

    // ---------- window, retention, truncation, unreadable jobs ----------

    [Fact]
    public async Task Window_UsesLookBack_OrTheShortestRetentionWindow()
    {
        Paid("recent");
        Paid("five-days", minutesAgo: 5 * 24 * 60);
        Paid("forty-days", minutesAgo: 40 * 24 * 60);
        var service = Service(Promise());

        var lookBack = await service.CheckAsync((await Single(service)).Id);
        Assert.Equal(2, lookBack.Counts.Watched);
        Assert.Equal(_now.AddDays(-30), lookBack.WindowStartUtc);

        var retained = Service(Promise(), retention: new JobRetentionOptions
        {
            DeleteSucceededAfter = TimeSpan.FromDays(7),
            DeleteFailedAfter = TimeSpan.FromDays(3),
        });
        var snapshot = await retained.CheckAsync((await Single(retained)).Id);
        Assert.Equal(1, snapshot.Counts.Watched);
        Assert.Equal(_now.AddDays(-3), snapshot.WindowStartUtc);
    }

    [Fact]
    public async Task Check_ReadsAtMostMaxRows_NewestFirst()
    {
        Paid("old", minutesAgo: 30);
        Paid("middle", minutesAgo: 20);
        Paid("new", minutesAgo: 10);
        var service = Service(Promise(), new TickerPromiseOptions { MaxRowsPerCheck = 2 });

        var snapshot = await service.CheckAsync((await Single(service)).Id);

        Assert.True(snapshot.Truncated);
        Assert.Equal(3, snapshot.Counts.Watched);
        Assert.Equal(["new", "middle"], snapshot.Findings.Select(f => f.Key));
    }

    [Fact]
    public async Task UnreadableJobs_WithholdNeverScheduled_AndSayWhy()
    {
        Paid("B");
        await _provider.AddTimeTickers([new Job
        {
            Id = Guid.NewGuid(), Function = Fn, Status = TickerStatus.Done, CreatedAt = _now,
            Request = Encoding.UTF8.GetBytes("not json"),
        }]);
        var service = Service(Promise());

        var snapshot = await service.CheckAsync((await Single(service)).Id);

        Assert.Equal("1 Promise.ProcessOrder job could not be read, so 'never scheduled' isn't reported this time.", snapshot.Error);
        Assert.Empty(snapshot.Findings);
        Assert.Equal(0, snapshot.Counts.NeedsReview);
    }

    [Fact]
    public async Task Settings_OverrideTheTimeLimit()
    {
        Paid("A", minutesAgo: 3);
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        Assert.Equal(0, (await service.CheckAsync(id)).Counts.Watched);

        var updated = await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { SetWithinSeconds = true, WithinSeconds = 60 }, "ann");
        Assert.Equal((60, 300), (updated.Settings.WithinSeconds.Value, updated.Settings.WithinSecondsFromCode));
        Assert.Contains("within 1 minute after", updated.Summary);
        Assert.Equal("ann set the time limit to 1 minute.", updated.Activity[0].Text);
        Assert.Equal(1, (await service.CheckAsync(id)).Counts.Watched);

        await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { SetWithinSeconds = true, WithinSeconds = null }, "ann");
        Assert.Equal(0, (await service.CheckAsync(id)).Counts.Watched);
        var invalid = await Assert.ThrowsAsync<TickerPromiseException>(() =>
            service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { SetWithinSeconds = true, WithinSeconds = 0 }, "ann"));
        Assert.Equal(400, invalid.StatusCode);
    }

    // ---------- fixes ----------

    [Fact]
    public async Task Fix_CreatesExactlyOneJob_WithTheTypedRequest()
    {
        Paid("B").Amount = 42.5m;
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        var finding = Assert.Single((await service.CheckAsync(id)).Findings);

        var result = await service.FixAsync(id, finding.Id, "ann");

        var job = Assert.Single(await JobsFor("B"));
        Assert.Equal(result.JobId, job.Id);
        Assert.Equal(TickerPromiseFix.NewJob, result.Fix);
        Assert.Equal(42.5m, TickerHelper.ReadTickerRequest(job.Request, TickerFunctionProvider.GetRequestTypeInfo<OrderRequest>(Fn)).Amount);
        Assert.Equal("Promise fix · Promise.ProcessOrder · Order · Order B · by ann", job.Description);
        Assert.Equal((0, 1), (result.Promise.Counts.NeedsReview, result.Promise.Counts.Running));
        Assert.Equal("ann ran a fix for Order B.", result.Promise.Activity[0].Text);

        // The row has a job now, so the finding is gone; a second click changes nothing.
        var again = await Assert.ThrowsAsync<TickerPromiseException>(() => service.FixAsync(id, finding.Id, "ann"));
        Assert.Equal(409, again.StatusCode);
        Assert.Single(await JobsFor("B"));
    }

    [Fact]
    public async Task Fix_RechecksFirst_AndRefusesAChangedRow()
    {
        var order = Paid("B");
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        var finding = Assert.Single((await service.CheckAsync(id)).Findings);
        order.Status = "Processed";

        var ex = await Assert.ThrowsAsync<TickerPromiseException>(() => service.FixAsync(id, finding.Id, "ann"));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("This row changed since it was checked. Refresh and try again.", ex.Message);
        Assert.Empty(await JobsFor("B"));
    }

    [Fact]
    public async Task RunNow_IsOfferedOnlyForAStandaloneJob_WithALocalDispatcher()
    {
        Paid("I"); var stuck = await AddJob("I", TickerStatus.Idle, 20);
        Paid("S");
        var root = NewJob("root", TickerStatus.InProgress, 20);
        root.Function = "Promise.Root";
        root.Children.Add(NewJob("S", TickerStatus.Idle, 20));
        await _provider.AddTimeTickers([root]);

        var service = Service(Promise());
        var id = (await Single(service)).Id;
        var snapshot = await service.CheckAsync(id);
        Assert.Equal(TickerPromiseFix.RunNow, Finding(snapshot, "I").Fix);
        Assert.Equal(TickerPromiseFix.None, Finding(snapshot, "S").Fix);
        await Assert.ThrowsAsync<TickerPromiseException>(() => service.FixAsync(id, Finding(snapshot, "S").Id, "ann"));

        var disabled = Substitute.For<ITickerQDispatcher>();
        var remote = Service(Promise(), dispatcher: disabled);
        Assert.Equal(TickerPromiseFix.None, Finding(await remote.CheckAsync(id), "I").Fix);

        var result = await service.FixAsync(id, Finding(snapshot, "I").Id, "ann");
        Assert.Equal((stuck.Id, TickerPromiseFix.RunNow), (result.JobId, result.Fix));
        Assert.Single(await JobsFor("I"));
    }

    [Fact]
    public async Task FixAll_FixesOpenFixableFindings()
    {
        Paid("N1"); Paid("N2"); Paid("X");
        Paid("D"); await AddJob("D", TickerStatus.Done);
        Paid("H"); await AddJob("H", TickerStatus.Cancelled);
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        var snapshot = await service.CheckAsync(id);
        await service.DismissAsync(id, Finding(snapshot, "X").Id, "ann", "a test order");

        var result = await service.FixAllAsync(id, "ann");

        Assert.Equal((3, 0, 0), (result.Fixed, result.Failed, result.Remaining));
        Assert.Single(await JobsFor("N1"));
        Assert.Single(await JobsFor("N2"));
        Assert.Equal(2, (await JobsFor("H")).Count);
        Assert.Empty(await JobsFor("X"));
        Assert.Equal(["D", "X"], result.Promise.Findings.Select(f => f.Key).Order());
        Assert.Equal((1, 1), (result.Promise.Counts.NeedsReview, result.Promise.Counts.Dismissed));
        Assert.Equal("ann ran fixes for 3 rows.", result.Promise.Activity[0].Text);
    }

    // ---------- dismissals ----------

    [Fact]
    public async Task Dismiss_IsKeptWhileTheFindingRecurs_AndPrunedOnceItDoesNot()
    {
        var b = Paid("B");
        Paid("C");
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        var finding = Finding(await service.CheckAsync(id), "B");

        var dismissed = await service.DismissAsync(id, finding.Id, "ann", " known issue ");
        Assert.Equal((1, 1), (dismissed.Counts.NeedsReview, dismissed.Counts.Dismissed));
        Assert.Equal(("ann", "known issue"), (Finding(dismissed, "B").Dismissed.By, Finding(dismissed, "B").Dismissed.Reason));
        Assert.Equal("ann dismissed Order B: known issue", dismissed.Activity[0].Text);

        Advance(TimeSpan.FromMinutes(1));
        Assert.NotNull(Finding(await service.CheckAsync(id), "B").Dismissed);

        b.Status = "Processed";
        Advance(TimeSpan.FromMinutes(1));
        await service.CheckAsync(id);
        b.Status = "Paid";
        Advance(TimeSpan.FromMinutes(1));
        var recurred = await service.CheckAsync(id);
        Assert.Null(Finding(recurred, "B").Dismissed);
        Assert.Equal((2, 0), (recurred.Counts.NeedsReview, recurred.Counts.Dismissed));
    }

    // ---------- scheduled checks: auto-fix, lease, events ----------

    [Fact]
    public async Task AutoFix_SchedulesOnlyNeverScheduledRows_Once()
    {
        Paid("N1");
        Paid("H"); await AddJob("H", TickerStatus.Cancelled);
        var service = Service(Promise(p => p.AutoFix(maxPerCheck: 2)));

        await service.CheckDueAsync();

        var job = Assert.Single(await JobsFor("N1"));
        Assert.StartsWith("Promise auto-fix · Promise.ProcessOrder · Order · Order N1", job.Description);
        Assert.Single(await JobsFor("H"));
        var snapshot = await Single(service);
        Assert.Equal(["H"], snapshot.Findings.Select(f => f.Key));
        Assert.Equal("Fixed 1 row that was never scheduled.", snapshot.Activity[0].Text);
        Assert.Contains(_sink.Events, e => e.Kind == "promise.auto_fixed");

        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        Assert.Single(await JobsFor("N1"));
    }

    [Fact]
    public async Task AutoFix_PausesOverTheLimit_UntilTurnedBackOn()
    {
        Paid("N1"); Paid("N2"); var n3 = Paid("N3");
        var service = Service(Promise(p => p.AutoFix(maxPerCheck: 2)));
        var id = (await Single(service)).Id;

        await service.CheckDueAsync();

        var paused = await Single(service);
        Assert.True(paused.AutoFix.Paused);
        Assert.Equal("3 Order rows were never scheduled — more than the limit of 2, so automatic fixing paused.", paused.AutoFix.PausedReason);
        Assert.Empty(await JobsFor("N1"));
        Assert.Single(_sink.Events, e => e.Kind == "promise.auto_fix_paused");

        // Still paused with fewer rows, until a person turns it back on.
        _shop.Orders.Remove(n3);
        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        Assert.Empty(await JobsFor("N1"));

        var resumed = await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { SetAutoFix = true, AutoFix = true }, "ann");
        Assert.False(resumed.AutoFix.Paused);
        Assert.Equal("ann resumed automatic fixing.", resumed.Activity[0].Text);
        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        Assert.Single(await JobsFor("N1"));
        Assert.Single(await JobsFor("N2"));
    }

    [Fact]
    public async Task AutoFix_CanBeTurnedOffOnTheDashboard()
    {
        Paid("N1");
        var service = Service(Promise(p => p.AutoFix()));
        var id = (await Single(service)).Id;
        var off = await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { SetAutoFix = true, AutoFix = false }, "ann");
        Assert.Equal((false, true), (off.AutoFix.Enabled, off.AutoFix.FromCode));

        await service.CheckDueAsync();

        Assert.Empty(await JobsFor("N1"));
    }

    [Fact]
    public async Task Lease_SecondNodeSkipsWhileHeld_AndTakesOverAfterExpiry()
    {
        Paid("B");
        var node = Service(Promise());
        var id = (await Single(node)).Id;
        var state = new TickerPromiseState { Lease = new TickerPromiseLease { Holder = "other-node", UntilUtc = _now.AddMinutes(1) } };
        await _provider.CreatePromiseStateAsync(new TickerPromiseStateRow
        {
            Id = Guid.ParseExact(id, "N"), Name = "Promise.ProcessOrder · Order",
            StateJson = JsonSerializer.Serialize(state, TickerPromiseJsonContext.Default.TickerPromiseState),
        });

        await node.CheckDueAsync();
        Assert.Null((await Single(node)).CheckedAtUtc);

        Advance(TimeSpan.FromMinutes(2));
        await node.CheckDueAsync();
        Assert.Equal(_now, (await Single(node)).CheckedAtUtc);

        // Checked moments ago, so another node leaves it alone.
        var other = Service(Promise());
        Advance(TimeSpan.FromSeconds(10));
        await other.CheckDueAsync();
        Assert.Equal(_now.AddSeconds(-10), (await Single(other)).CheckedAtUtc);
    }

    [Fact]
    public async Task Events_ArePublishedOnlyOnTransitions_ByTheScheduledCheck()
    {
        var b = Paid("B");
        var service = Service(Promise());
        var id = (await Single(service)).Id;

        await service.CheckAsync(id); // a person's check publishes nothing and hides no transition
        Assert.Empty(_sink.Events);

        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        var needsReview = Assert.Single(_sink.Events);
        Assert.Equal(("promise.needs_review", TickerQEventSeverity.Warning, "1"), (needsReview.Kind, needsReview.Severity, needsReview.Data["needsReview"]));
        Assert.Equal($"promises/{id}", needsReview.DashboardPath);
        Assert.Equal("Promise.ProcessOrder · Order: 1 row needs review", needsReview.Title);

        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        Assert.Single(_sink.Events);

        b.Status = "Processed";
        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        Assert.Equal("promise.recovered", _sink.Events[^1].Kind);

        _shop.Fail = true;
        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        Assert.Equal("promise.check_failed", _sink.Events[^1].Kind);
        Assert.Equal("The check failed: database is down", _sink.Events[^1].Detail);
        Advance(TimeSpan.FromMinutes(1));
        await service.CheckDueAsync();
        Assert.Equal(3, _sink.Events.Count);
        Assert.Equal(3, _sink.Events.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public async Task Paused_PromiseIsNotCheckedOnSchedule()
    {
        Paid("B");
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { Paused = true }, "ann");

        await service.CheckDueAsync();

        Assert.Null((await Single(service)).CheckedAtUtc);
        Assert.NotNull((await service.CheckAsync(id)).CheckedAtUtc);
    }

    [Fact]
    public async Task RowId_DependsOnThePartition()
    {
        var a = (await Single(Service(Promise(), partition: "a"))).Id;
        Assert.Equal(a, (await Single(Service(Promise(), partition: "a"))).Id);
        Assert.NotEqual(a, (await Single(Service(Promise(), partition: "b"))).Id);
        var missing = await Assert.ThrowsAsync<TickerPromiseException>(() => Service(Promise()).CheckAsync("nope"));
        Assert.Equal(404, missing.StatusCode);
    }

    // ---------- declaration ----------

    [Fact]
    public void MapTicker_ReturnsTypedBuilder_ThatRegistersPromises()
    {
        var services = new ServiceCollection();
        services.MapTicker<PromiseOrderJob, OrderRequest>()
            .WithPriority(TickerTaskPriority.High)
            .Promise((Shop s) => s.Query(), p => p
                .Within(TimeSpan.FromMinutes(1), o => o.PaidAt)
                .Job(o => new OrderRequest { OrderId = o.Number }, r => r.OrderId));
        var options = new TickerPromiseOptions();
        options.For<PromiseOrderJob, OrderRequest>()
            .Promise(sp => sp.GetRequiredService<Shop>().Query(), p => p.Named("By type"));
        options.For(new TickerFunctionRef<OrderRequest>(Fn), (Shop s) => s.Query(), p => p.Named("By reference"));

        var registered = Assert.Single(services, d => d.ServiceType == typeof(TickerPromiseDefinition));
        var definition = (TickerPromiseDefinition)registered.ImplementationInstance;
        Assert.Equal(("PromiseOrderJob", "PromiseOrderJob · Order", typeof(Shop)), (definition.Function, definition.Name, definition.ContextType));
        Assert.Equal((typeof(PromiseOrderJob), TickerPromiseDeclaration.MapTicker), (definition.FunctionType, definition.DeclaredOn));
        Assert.Equal(TickerPromiseDeclaration.ConfigurePromises, options.Definitions[0].DeclaredOn);
        Assert.Equal(["PromiseOrderJob", Fn], options.Definitions.Select(d => d.Function));
        Assert.Null(options.Definitions[0].ContextType);
    }

    [Fact]
    public void StartupValidation_NamesEveryProblem()
    {
        var isService = new ServiceCollection().AddSingleton(_shop).BuildServiceProvider().GetRequiredService<IServiceProviderIsService>();
        var definitions = new[]
        {
            TickerPromiseDefinition.Create<Shop, Order, OrderRequest>("Promise.Missing", null, s => s.Query(), p => p.Named("unknown")),
            TickerPromiseDefinition.Create<Shop, Order, OtherRequest>(Fn, null, s => s.Query(), p => p.Named("wrong request")),
            TickerPromiseDefinition.Create<NotRegistered, Order, OrderRequest>(Fn, null, s => s.Orders, p => p.Named("Same")),
            TickerPromiseDefinition.Create<Shop, Order, OrderRequest>(Fn, null, s => s.Query(), p => p.Named("same")),
            TickerPromiseDefinition.Create<Shop, Order, OrderRequest>(null, typeof(UnmappedJob), s => s.Query(), p => p.Named("unmapped")),
        };

        var message = Assert.Throws<InvalidOperationException>(() => TickerQPromiseStartupValidator.Validate(definitions, isService)).Message;

        Assert.Contains("Promise 'unknown' needs .Within(...)", message);
        Assert.Contains("Promise 'unknown' needs .Job(...)", message);
        Assert.Contains("Promise 'unknown': no ticker function named 'Promise.Missing' is registered.", message);
        Assert.Contains("Promise 'wrong request': Promise.ProcessOrder takes a OrderRequest, not a OtherRequest.", message);
        Assert.Contains("Promise 'Same' reads its rows from NotRegistered, which is not registered as a service.", message);
        Assert.Contains("2 promises are named 'Same'.", message);
        Assert.Contains("both watch Order rows for Promise.ProcessOrder", message);
        Assert.Contains("Promise 'unmapped': UnmappedJob is not registered as a ticker function. Register it with MapTicker<UnmappedJob, OrderRequest>().", message);

        TickerQPromiseStartupValidator.Validate([Promise()], isService);
    }

    // ---------- jobs ----------

    [Fact]
    public async Task Jobs_ListTheFunctionsJobs_WithWhoScheduledThem_AndTotalsForTheWindow()
    {
        Paid("B");
        Paid("C"); await AddJob("C", TickerStatus.Failed, configure: j =>
        {
            j.Description = "Charge C";
            j.ExceptionMessage = "{\"Message\":\"card declined\"}";
        });
        Paid("D"); await AddJob("D", TickerStatus.Skipped, configure: j => j.SkippedReason = "outside business hours");
        await AddJob("old", TickerStatus.Done, minutesAgo: 60 * 24 * 40); // before the window
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        var finding = Finding(await service.CheckAsync(id), "B");
        var fix = await service.FixAsync(id, finding.Id, "ann");

        var list = await service.JobsAsync(id);

        // The fix job is due now, so the dispatcher may already have started it.
        Assert.Equal((0, 1, 1, 1), (list.Totals.Done, list.Totals.Failed, list.Totals.Running + list.Totals.Waiting, list.Totals.Stopped));
        Assert.Equal(_now - _options.LookBack, list.WindowStartUtc);
        Assert.Equal(["B", "C", "D"], list.Jobs.Select(j => j.Key).Order());
        var fixJob = Assert.Single(list.Jobs, j => j.Key == "B");
        Assert.Equal((fix.JobId, TickerPromiseJobSource.Fix, "ann", "Order B"), (fixJob.Id, fixJob.Source, fixJob.FixedBy, fixJob.Label));
        var failed = Assert.Single(list.Jobs, j => j.Key == "C");
        Assert.Equal((TickerPromiseJobSource.App, "Charge C", "card declined"), (failed.Source, failed.Label, failed.Error));
        Assert.Equal("outside business hours", Assert.Single(list.Jobs, j => j.Key == "D").Error);
        await Assert.ThrowsAsync<TickerPromiseException>(() => service.JobsAsync("missing"));
    }

    [Fact]
    public async Task Snapshot_DescribesTheRuleAsDeclaredInCode_WithTheCheckOptions()
    {
        var service = Service(Promise(p => p.AutoFix(5).Named("Paid orders")));

        var rule = (await Single(service)).Rule;

        Assert.Equal(("Order", "Shop", null, "OrderRequest"), (rule.RowType, rule.RowsFrom, rule.FunctionClass, rule.RequestType));
        Assert.Equal(TickerPromiseDeclaration.ConfigurePromises, rule.DeclaredOn);
        Assert.Equal(["OrderId"], rule.MatchBy);
        Assert.Equal((300, "PaidAt", "Status is Processed"), (rule.WithinSeconds, rule.After, rule.DoneWhen));
        Assert.Equal((true, "Paid orders", 5, false), (rule.HasLabel, rule.NamedInCode, rule.AutoFixMax, rule.AllowRepeats));
        Assert.Equal((60, 600, 1000, 50_000, 30d, false), (rule.Checks.CheckIntervalSeconds, rule.Checks.NeverRanAfterSeconds,
            rule.Checks.MaxRowsPerCheck, rule.Checks.MaxJobsPerCheck, rule.Checks.LookBackDays, rule.Checks.LookBackFromRetention));
    }

    // ---------- the job index: reading only what changed ----------

    [Fact]
    public async Task Index_SeesJobsThatFinishedWereCancelledOrArrived_SinceTheLastCheck()
    {
        Paid("A"); var a = await AddJob("A", TickerStatus.Idle, -5);  // waiting, due in 5 minutes
        Paid("B"); var b = await AddJob("B", TickerStatus.Idle, -5);
        Paid("C");                                                    // never scheduled
        var service = Service(Promise(doneWhen: false));
        var id = (await Single(service)).Id;
        var first = await service.CheckAsync(id);
        Assert.Equal((0, 2, 1), (first.Counts.Done, first.Counts.Running, first.Counts.NeedsReview));

        // Since then: A finished, B was cancelled, and C's job arrived and already ran.
        Advance(TimeSpan.FromMinutes(1));
        await SetStatus(a, TickerStatus.Done);
        await SetStatus(b, TickerStatus.Cancelled);
        await AddJob("C", TickerStatus.Done, 0, j => j.ExecutedAt = _now);

        var second = await service.CheckAsync(id);
        Assert.Equal((2, 0, 1), (second.Counts.Done, second.Counts.Running, second.Counts.NeedsReview));
        Assert.Equal((TickerPromiseFindingKind.NeverRan, TickerStatus.Cancelled), (Finding(second, "B").Kind, Finding(second, "B").LastJob.Status));
    }

    [Fact]
    public async Task Index_ForgetsAWaitingJobThatWasDeleted_AndAFinishedOneOnTheNextReload()
    {
        Paid("A"); var a = await AddJob("A", TickerStatus.Idle, -5);
        Paid("B"); var b = await AddJob("B", TickerStatus.Done, 0, j => j.ExecutedAt = _now);
        var service = Service(Promise(doneWhen: false));
        var id = (await Single(service)).Id;
        var first = await service.CheckAsync(id);
        Assert.Equal((1, 1, 0), (first.Counts.Done, first.Counts.Running, first.Counts.NeedsReview));

        Advance(TimeSpan.FromMinutes(1));
        await _provider.RemoveTimeTickers([a.Id, b.Id]);

        // A was waiting, so the index asked after it and let it go; B had finished, so it is kept until the window is read again.
        var second = await service.CheckAsync(id);
        Assert.Equal((1, 0, 1), (second.Counts.Done, second.Counts.Running, second.Counts.NeedsReview));
        Assert.Equal(TickerPromiseFindingKind.NoJob, Finding(second, "A").Kind);

        Advance(_options.FullReloadInterval);
        var third = await service.CheckAsync(id);
        Assert.Equal((0, 0, 2), (third.Counts.Done, third.Counts.Running, third.Counts.NeedsReview));
    }

    [Fact]
    public async Task Index_SeesAFinishedJobRunAgain_AndFinishAgain()
    {
        Paid("A"); var a = await AddJob("A", TickerStatus.Done, 0, j => j.ExecutedAt = _now);
        var service = Service(Promise(doneWhen: false));
        var id = (await Single(service)).Id;
        Assert.Equal(1, (await service.CheckAsync(id)).Counts.Done);

        // Run again from the dashboard: the same job is queued once more.
        Advance(TimeSpan.FromMinutes(1));
        await SetStatus(a, TickerStatus.Queued, finished: false);
        var again = await service.CheckAsync(id);
        Assert.Equal((0, 1), (again.Counts.Done, again.Counts.Running));

        Advance(TimeSpan.FromMinutes(1));
        await SetStatus(a, TickerStatus.Failed);
        var failed = await service.CheckAsync(id);
        Assert.Equal((0, 0, 1), (failed.Counts.Done, failed.Counts.Running, failed.Counts.Failed));
    }

    [Fact]
    public async Task Index_KeepsRowsCoveredWhenMoreJobsThanItHoldsArrive()
    {
        for (var i = 0; i < 100; i++)
            await AddJob($"N{i}", TickerStatus.Done, 60 * 24 * 2, j => j.ExecutedAt = j.ExecutionTime);
        Paid("NEW");
        var options = new TickerPromiseOptions { CheckInterval = TimeSpan.FromMinutes(1), MaxJobsPerCheck = 100 };
        var service = Service(Promise(), options);
        var id = (await Single(service)).Id;
        var first = await service.CheckAsync(id);
        Assert.Equal((false, "NEW"), (first.Truncated, Assert.Single(first.Findings).Key));

        // Five more jobs than the index holds: the oldest fall out and the window narrows to what is still covered.
        Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 5; i++)
            await AddJob($"M{i}", TickerStatus.Done, 0, j => j.ExecutedAt = _now);
        var second = await service.CheckAsync(id);
        Assert.True(second.Truncated);
        // The oldest kept jobs are two days old, so rows from a day after them are covered.
        Assert.Equal(_now.AddMinutes(-1).AddDays(-1), second.WindowStartUtc);
        Assert.Equal("NEW", Assert.Single(second.Findings).Key);
    }

    [Fact]
    public async Task Index_ReadsARequestAgain_WhenAWaitingJobWasEdited()
    {
        Paid("A"); var a = await AddJob("A", TickerStatus.Idle, -5);
        Paid("B");
        var service = Service(Promise(doneWhen: false));
        var id = (await Single(service)).Id;
        var first = await service.CheckAsync(id);
        Assert.Equal(("B", 1), (Assert.Single(first.Findings).Key, first.Counts.Running));

        // Someone corrected the job's request from order A to order B.
        Advance(TimeSpan.FromMinutes(1));
        await EditRequest(_provider, a, "B");
        var second = await service.CheckAsync(id);
        Assert.Equal(("A", 1), (Assert.Single(second.Findings).Key, second.Counts.Running));
    }

    [Fact]
    public async Task Index_WithAChangeFeed_ReadsOnlyWhatWasWritten_AndFollowsEveryChange()
    {
        var provider = new FeedProvider(_services);
        Paid("A"); var a = NewJob("A", TickerStatus.Idle, -5); await provider.AddTimeTickers([a]);
        Paid("B"); var b = NewJob("B", TickerStatus.Idle, -5); await provider.AddTimeTickers([b]);
        Paid("C"); var c = NewJob("C", TickerStatus.Done); c.ExecutedAt = _now; await provider.AddTimeTickers([c]);
        Paid("D");
        var service = Service(Promise(doneWhen: false), provider: provider);
        var id = (await Single(service)).Id;
        var first = await service.CheckAsync(id);
        Assert.Equal((1, 2, 1, 0), (first.Counts.Done, first.Counts.Running, first.Counts.NeedsReview, provider.FeedReads));

        // A finished, B was deleted, C's request was edited to D, and all of it comes from the feed, not a scan.
        Advance(TimeSpan.FromMinutes(1));
        await provider.RemoveTimeTickers([a.Id]);
        var done = NewJob("A", TickerStatus.Done); done.Id = a.Id; done.CreatedAt = a.CreatedAt; done.ExecutedAt = _now;
        await provider.AddTimeTickers([done]);
        await provider.RemoveTimeTickers([b.Id]);
        await EditRequest(provider, c, "D");
        var second = await service.CheckAsync(id);
        Assert.Equal((2, 0, 2, 1), (second.Counts.Done, second.Counts.Running, second.Counts.NeedsReview, provider.FeedReads));
        Assert.Equal(["B", "C"], second.Findings.Select(f => f.Key).Order());
        Assert.All(second.Findings, f => Assert.Equal(TickerPromiseFindingKind.NoJob, f.Kind));

        // More aggregates written than the feed may hand back: the window is read again instead.
        Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 101; i++) await provider.AddTimeTickers([NewJob($"X{i}", TickerStatus.Done)]);
        var options = new TickerPromiseOptions { CheckInterval = TimeSpan.FromMinutes(1), MaxJobsPerCheck = 100 };
        var capped = Service(Promise(doneWhen: false), options, provider: provider);
        var third = await capped.CheckAsync((await Single(capped)).Id);
        Assert.True(third.Truncated);
    }

    [Fact]
    public async Task Index_WithoutDeltaReads_ReadsTheWindowEachCheck_AndAgreesWithTheIncrementalPath()
    {
        var provider = new ReloadOnlyProvider(_services);
        Paid("A"); var a = NewJob("A", TickerStatus.Idle, -5); await provider.AddTimeTickers([a]);
        Paid("B");
        var service = Service(Promise(doneWhen: false), provider: provider);
        var id = (await Single(service)).Id;
        var first = await service.CheckAsync(id);
        Assert.Equal((0, 1, 1), (first.Counts.Done, first.Counts.Running, first.Counts.NeedsReview));

        Advance(TimeSpan.FromMinutes(1));
        await provider.RemoveTimeTickers([a.Id]);
        var done = NewJob("A", TickerStatus.Done); done.Id = a.Id; done.ExecutedAt = _now;
        await provider.AddTimeTickers([done]);
        var second = await service.CheckAsync(id);
        Assert.Equal((1, 0, 1), (second.Counts.Done, second.Counts.Running, second.Counts.NeedsReview));
    }

    // ---------- real-life timing and scale ----------

    [Fact]
    public async Task Snapshot_SaysWhenTheNextCheckIsDue_AndNothingWhilePaused()
    {
        var service = Service(Promise());
        var id = (await Single(service)).Id;
        Assert.Equal((_now + _options.CheckInterval, 60), ((await Single(service)).NextCheckAtUtc, (await Single(service)).CheckIntervalSeconds));

        var checkedAt = _now;
        Assert.Null((await Single(service)).CreatedAtUtc);
        await service.CheckAsync(id);
        Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(checkedAt + _options.CheckInterval, (await Single(service)).NextCheckAtUtc);
        Assert.Equal(checkedAt, (await Single(service)).CreatedAtUtc); // kept from the first save

        var paused = await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { Paused = true }, "ann");
        Assert.Null(paused.NextCheckAtUtc);

        // Enabled again an hour later: the next check counts from then, so it isn't reported late.
        Advance(TimeSpan.FromHours(1));
        var enabled = await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { Paused = false }, "ann");
        Assert.Equal(_now + _options.CheckInterval, enabled.NextCheckAtUtc);
        Assert.Equal("ann enabled the promise.", enabled.Activity[0].Text);
    }

    [Fact]
    public async Task Check_GivesARowTheGraceToBeUpdated_AfterItsJobSucceeded()
    {
        Paid("A"); await AddJob("A", TickerStatus.Done, configure: j => j.ExecutedAt = _now.AddSeconds(-20));
        var service = Service(Promise());
        var id = (await Single(service)).Id;

        var snapshot = await service.CheckAsync(id);
        Assert.Equal((1, 0), (snapshot.Counts.Running, snapshot.Counts.NeedsReview));

        Advance(TimeSpan.FromMinutes(1));
        snapshot = await service.CheckAsync(id);
        Assert.Equal(TickerPromiseFindingKind.DoneButNotApplied, Finding(snapshot, "A").Kind);
    }

    [Fact]
    public async Task Check_WithMoreJobsThanItReads_ChecksOnlyTheRowsTheJobsCover()
    {
        // Job "OLD" is the oldest and falls outside the cap, so its row must not look never scheduled.
        Paid("OLD", minutesAgo: 60 * 24 * 5); await AddJob("OLD", TickerStatus.Done, 60 * 24 * 5);
        for (var i = 0; i < 100; i++)
            await AddJob($"N{i}", TickerStatus.Done, 60 * 24 * 2);
        Paid("NEW"); // never scheduled, inside what the jobs read cover
        var service = Service(Promise(), new TickerPromiseOptions { CheckInterval = TimeSpan.FromMinutes(1), MaxJobsPerCheck = 100 });

        var snapshot = await service.CheckAsync((await Single(service)).Id);

        Assert.True(snapshot.Truncated);
        Assert.Equal(_now.AddDays(-1), snapshot.WindowStartUtc);
        Assert.Equal("NEW", Assert.Single(snapshot.Findings).Key);
    }

    // ---------- duplicates ----------

    [Fact]
    public async Task Duplicates_ReportRowsThatRanTwice_EvenWhenDone_AndCancelAnExtraWaitingJob()
    {
        Paid("A", status: "Processed"); await AddJob("A", TickerStatus.Done, 30); await AddJob("A", TickerStatus.Done, 20);
        Paid("B", status: "Processed"); await AddJob("B", TickerStatus.Done, 30);
        var extra = await AddJob("B", TickerStatus.Idle, -60);                       // due in an hour, after B already ran
        Paid("C", status: "Processed"); await AddJob("C", TickerStatus.Failed, 30); await AddJob("C", TickerStatus.Done, 20); // a fix after a failure
        Paid("D", status: "Processed"); await AddJob("D", TickerStatus.Cancelled, 30); await AddJob("D", TickerStatus.Done, 20);
        var service = Service(Promise());
        var id = (await Single(service)).Id;

        var snapshot = await service.CheckAsync(id);

        Assert.Equal((2, 2), (snapshot.Counts.NeedsReview, snapshot.Counts.Duplicates));
        Assert.True(snapshot.ChecksDuplicates);
        var ranTwice = Finding(snapshot, "A");
        Assert.Equal((TickerPromiseFindingKind.RanTwice, TickerPromiseFix.None), (ranTwice.Kind, ranTwice.Fix));
        Assert.Contains("succeeded 2 times", ranTwice.Reason);
        var willRunTwice = Finding(snapshot, "B");
        Assert.Equal((TickerPromiseFindingKind.WillRunTwice, TickerPromiseFix.CancelExtra, extra.Id),
            (willRunTwice.Kind, willRunTwice.Fix, willRunTwice.LastJob.Id));
        Assert.Equal("An extra job is waiting, though the job already succeeded.", willRunTwice.Reason);

        var fix = await service.FixAsync(id, willRunTwice.Id, "ann");

        Assert.Equal(extra.Id, fix.JobId);
        Assert.Equal(TickerStatus.Cancelled, (await JobsFor("B")).Single(j => j.Id == extra.Id).Status);
        // B is done, so its row isn't read and the finding names it by its match value.
        Assert.Equal("ann cancelled the extra job for B.", fix.Promise.Activity[0].Text);
        Assert.Equal((1, 1, 0), (fix.Promise.Counts.NeedsReview, fix.Promise.Counts.Duplicates, fix.Promise.Counts.Running));
    }

    [Fact]
    public async Task Duplicates_KeepTheFirstOfTwoWaitingJobs_AndAreSkippedWithAllowRepeats()
    {
        Paid("A");
        var first = await AddJob("A", TickerStatus.Idle, -5);
        var second = await AddJob("A", TickerStatus.Idle, -10);
        second.CreatedAt = first.CreatedAt.AddSeconds(1);

        var finding = Finding(await Service(Promise()).CheckAsync((await Single(Service(Promise()))).Id), "A");
        Assert.Equal((TickerPromiseFindingKind.WillRunTwice, second.Id), (finding.Kind, finding.LastJob.Id));

        var allowed = Service(Promise(p => p.AllowRepeats()));
        var snapshot = await allowed.CheckAsync((await Single(allowed)).Id);
        Assert.Empty(snapshot.Findings);
        Assert.False(snapshot.ChecksDuplicates);
    }

    // ---------- analytics ----------

    [Fact]
    public async Task Analytics_CountJobsPerLocalDay_WithSourcesAndDurations()
    {
        await AddJob("A", TickerStatus.Done, 60, j => { j.ExecutedAt = j.ExecutionTime; j.ElapsedTime = 100; });
        await AddJob("B", TickerStatus.Failed, 60, j => { j.ExecutedAt = j.ExecutionTime; j.ElapsedTime = 300; });
        await AddJob("C", TickerStatus.Done, 60 * 24 + 60, j => { j.ExecutedAt = j.ExecutionTime; j.ElapsedTime = 50; j.Description = "Promise fix · x · C · by ann"; });
        await AddJob("D", TickerStatus.Cancelled, 60 * 24 + 60, j => j.Description = "Promise auto-fix · x · D");
        await AddJob("E", TickerStatus.Done, 60 * 24 * 5);                                // before the range
        var service = Service(Promise());
        var id = (await Single(service)).Id;

        var analytics = Assert.Single(await service.AnalyticsAsync(id, 3, 120));

        // 12:00 UTC is 14:00 at +02:00, so the range is 13–15 Nov local.
        Assert.Equal(["2026-11-13", "2026-11-14", "2026-11-15"], analytics.Daily.Select(d => d.Day));
        Assert.Equal(new DateTime(2026, 11, 12, 22, 0, 0, DateTimeKind.Utc), analytics.FromUtc);
        var (yesterday, today) = (analytics.Daily[1], analytics.Daily[2]);
        Assert.Equal((1, 0, 1, 1, 1), (yesterday.Done, yesterday.Failed, yesterday.Stopped, yesterday.Fixes, yesterday.AutoFixes));
        Assert.Equal((1, 1, 0, 200L, 300L), (today.Done, today.Failed, today.Fixes, today.AverageMs, today.P95Ms));
        Assert.Equal((2, 1, 1), (analytics.Sources.App, analytics.Sources.Fix, analytics.Sources.AutoFix));
        Assert.Equal((150L, 300L), (analytics.AverageMs, analytics.P95Ms));
        Assert.Single(await service.AnalyticsAsync(null, 3, 0));
        Assert.Equal(400, (await Assert.ThrowsAsync<TickerPromiseException>(() => service.AnalyticsAsync(id, 0, 0))).StatusCode);
    }

    // ---------- bounded stores ----------

    [Fact]
    public async Task BoundedStore_KeepsTheNewestFindingsThatFit_WithCountsSettingsAndDismissals()
    {
        for (var i = 0; i < 40; i++)
            Paid($"ORD-{i:D2}", minutesAgo: 10 + i);
        var provider = new BoundedProvider(_services);
        var service = Service(Promise(), provider: provider);
        var id = (await Single(service)).Id;

        var snapshot = await service.CheckAsync(id);
        snapshot = await service.DismissAsync(id, Finding(snapshot, "ORD-00").Id, "ann", "a test order");
        snapshot = await service.UpdateSettingsAsync(id, new TickerPromiseSettingsChange { SetWithinSeconds = true, WithinSeconds = 120 }, "ann");
        snapshot = await service.CheckAsync(id);

        var row = Assert.Single(await provider.ListDefinitionsAsync(SavedTickerDefinitionKind.Promise));
        Assert.InRange(row.DefinitionJson.Length, 1, 2000);
        Assert.Empty(await ((ITickerPromiseStateStore)provider).ListPromiseStatesAsync());
        Assert.Equal((40, 39, 1), (snapshot.Counts.Watched, snapshot.Counts.NeedsReview, snapshot.Counts.Dismissed));
        Assert.Equal(120, snapshot.Settings.WithinSeconds);
        Assert.InRange(snapshot.Findings.Count, 1, 10);
        Assert.Equal(Enumerable.Range(0, snapshot.Findings.Count).Select(i => $"ORD-{i:D2}"), snapshot.Findings.Select(f => f.Key));
        Assert.Equal("a test order", Finding(snapshot, "ORD-00").Dismissed.Reason);
        Assert.Equal(snapshot.Findings.Count, (await Single(service)).Findings.Count);
    }

    [Fact]
    public void Builder_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Promise(p => p.Within(TimeSpan.Zero, o => o.PaidAt)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Promise(p => p.AutoFix(0)));
        Assert.Throws<ArgumentException>(() => Promise(p => p.Named(" ")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickerPromiseOptions { CheckInterval = TimeSpan.FromSeconds(1) }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickerPromiseOptions { FullReloadInterval = TimeSpan.FromSeconds(30) }.Validate());
    }
}
