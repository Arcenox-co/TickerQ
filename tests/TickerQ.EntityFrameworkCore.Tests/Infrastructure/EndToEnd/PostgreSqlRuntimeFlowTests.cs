using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.EndToEnd;

/// <summary>
/// The whole engine on PostgreSQL: discovery, claim, workers, gated outcome writes, chains,
/// retries, shutdown, and recovery, with more due work than worker slots.
/// </summary>
[Collection("PostgreSql end-to-end")]
public sealed class PostgreSqlRuntimeFlowTests(PostgreSqlEndToEndServer server)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    [PostgreSqlRaceFact]
    public async Task Burst_RunsEveryJobOnce_WithStoreConnectionsBoundedByMaxConcurrency()
    {
        const int jobs = 500;
        const int maxConcurrency = 4;
        var connectionString = await server.CreateDatabaseAsync();
        var function = Unique("burst");
        var runs = new ConcurrentDictionary<Guid, int>();

        await using var host = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "burst",
            Scheduler = scheduler => scheduler.MaxConcurrency = maxConcurrency,
            Services = services => services.MapTicker(function, (context, _) =>
            {
                runs.AddOrUpdate(context.Id, 1, (_, count) => count + 1);
                return Task.CompletedTask;
            }),
        });

        await using var connections = new ConnectionSampler(server, connectionString);
        var ids = await AddDueAsync(host, function, jobs);
        await WaitForStatusAsync(host, ids, Succeeded);
        var peak = await connections.StopAsync();

        Assert.Equal(jobs, runs.Count);
        Assert.All(ids, id => Assert.Equal(1, runs[id]));
        // Outcome writes run after a job leaves its worker slot; without the write gate every
        // finished job in the burst opens its own transaction, which is what exhausted the server.
        // The remainder covers the scheduler loop, lease renewal, recovery, and activation monitor.
        Assert.True(peak.Count <= maxConcurrency + 8, $"The host held {peak} for MaxConcurrency {maxConcurrency}.");
    }

    [PostgreSqlRaceFact]
    public async Task TwoNodes_OnOneDatabase_RunEachJobOnce_AndEachRowRecordsTheNodeThatRanIt()
    {
        const int jobs = 400;
        var connectionString = await server.CreateDatabaseAsync();
        var function = Unique("shared");
        var ranOn = new ConcurrentDictionary<Guid, ConcurrentQueue<string>>();
        Action<Utilities.SchedulerOptionsBuilder> scheduler = options =>
        {
            options.MaxConcurrency = 4;
            // Small claims so both nodes keep competing for the same due rows.
            options.AcquisitionBatchSize = 20;
        };

        await using var a = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "node-a",
            Scheduler = scheduler,
            Services = services => services.MapTicker(function, (context, provider, _) =>
            {
                ranOn.GetOrAdd(context.Id, _ => new()).Enqueue(provider.GetRequiredService<NodeLabel>().Name);
                return Task.CompletedTask;
            }),
        });
        await using var b = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "node-b",
            CreateTables = false,
            Scheduler = scheduler,
        });

        var ids = await AddDueAsync(a, function, jobs);
        await WaitForStatusAsync(a, ids, Succeeded);

        Assert.Equal(jobs, ranOn.Count);
        Assert.All(ids, id => Assert.Single(ranOn[id]));
        var perNode = ranOn.Values.GroupBy(x => x.Single()).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(perNode.Count == 2, $"Only {string.Join(", ", perNode.Keys)} ran jobs.");

        var owners = new Dictionary<string, string> { ["node-a"] = a.ExecutionOwnerId(), ["node-b"] = b.ExecutionOwnerId() };
        var rows = await RowsAsync(a, ids);
        Assert.All(rows, row => Assert.Equal(owners[ranOn[row.Id].Single()], row.LockHolder));
    }

    // Every job has a worker slot, so stopping node-a abandons only running functions.
    [PostgreSqlRaceFact]
    public Task NodeStoppedMidBurst_LeavesNothingStuck_AndAnotherNodeNeverRerunsWhatItCommitted()
        => StopMidBurstThenRecoverAsync(maxConcurrency: 60, maxDurationMs: 3000, observesCancellation: false);

    // The stop cancels the token of every execution node-a admitted: jobs still waiting for a worker
    // slot and functions that honour the token. Neither may be recorded as Cancelled, or a backlog
    // that outlives the drain window during a deploy would never run.
    [PostgreSqlRaceFact]
    public Task NodeStoppedWithABacklog_ReleasesWaitingJobsAndRestartsRunningOnes_InsteadOfCancellingThem()
        => StopMidBurstThenRecoverAsync(maxConcurrency: 2, maxDurationMs: 300, observesCancellation: true);

    [PostgreSqlRaceFact]
    public async Task JobCancelledByAUserWhileItsNodeDrains_StaysCancelled_AndNoNodeRunsItAgain()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var function = Unique("drain-cancel");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        Action<Utilities.SchedulerOptionsBuilder> scheduler = options =>
        {
            options.ShutdownDrainTimeout = TimeSpan.FromSeconds(20);
            options.LeaseDuration = TimeSpan.FromSeconds(3);
            options.LeaseRenewalInterval = TimeSpan.FromSeconds(1);
            options.FallbackIntervalChecker = TimeSpan.FromSeconds(2);
        };

        var a = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "node-a",
            Scheduler = scheduler,
            Services = services => services.MapTicker(function, async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref runs);
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }),
        });
        var id = (await a.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>()
            .AddAsync(new TimeTickerEntity { Function = function, ExecutionTime = DateTime.UtcNow })).EnsureSuccess().Result.Id;
        await started.Task.WaitAsync(Deadline);

        // The deploy begins: node-a stops admitting work and drains, with the job still running.
        var stopping = a.StopAsync();
        var workers = a.Services.GetRequiredService<Utilities.Interfaces.ITickerQTaskScheduler>();
        await EndToEndHost.WaitUntilAsync(() => Task.FromResult(workers.IsFrozen), Deadline, "node-a to start draining");
        (await a.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().RequestCancellationAsync(id))
            .EnsureSuccess();
        await stopping;
        Assert.Equal(TickerStatus.Cancelled, (await RowsAsync(a, [id])).Single().Status);
        await a.DisposeAsync();

        // Past the lease and a fallback scan, another node still treats it as finished.
        await using var b = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "node-b",
            CreateTables = false,
            Scheduler = scheduler,
        });
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.Equal(TickerStatus.Cancelled, (await RowsAsync(b, [id])).Single().Status);
        Assert.Equal(1, runs);
    }

    private async Task StopMidBurstThenRecoverAsync(int maxConcurrency, int maxDurationMs, bool observesCancellation)
    {
        const int jobs = 60;
        var connectionString = await server.CreateDatabaseAsync();
        var function = Unique("drain");
        var completedOn = new ConcurrentDictionary<Guid, ConcurrentQueue<string>>();
        Action<Utilities.SchedulerOptionsBuilder> scheduler = options =>
        {
            options.MaxConcurrency = maxConcurrency;
            // No drain: running and queued work is abandoned, as on a crash, for recovery to heal.
            options.ShutdownDrainTimeout = TimeSpan.Zero;
            options.LeaseDuration = TimeSpan.FromSeconds(3);
            options.LeaseRenewalInterval = TimeSpan.FromSeconds(1);
            options.QueuedLockTimeout = TimeSpan.FromSeconds(3);
            // Released and recovered jobs are overdue by then, which the fallback scan picks up.
            options.FallbackIntervalChecker = TimeSpan.FromSeconds(2);
        };

        var a = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "node-a",
            Scheduler = scheduler,
            Services = services => services.MapTicker(function, async (context, provider, cancellationToken) =>
            {
                // Spread the finishes so node-a is stopped with some jobs done and others running.
                await Task.Delay(Random.Shared.Next(100, maxDurationMs), observesCancellation ? cancellationToken : CancellationToken.None);
                completedOn.GetOrAdd(context.Id, _ => new()).Enqueue(provider.GetRequiredService<NodeLabel>().Name);
            }),
        });
        var ownerA = a.ExecutionOwnerId();
        var ids = await AddDueAsync(a, function, jobs);
        await EndToEndHost.WaitUntilAsync(async () => (await RowsAsync(a, ids)).Count(x => Succeeded.Contains(x.Status)) >= 10,
            Deadline, "node-a to finish some jobs");

        await a.StopAsync();
        var committedByA = (await RowsAsync(a, ids)).Where(x => Succeeded.Contains(x.Status)).Select(x => x.Id).ToHashSet();
        await a.DisposeAsync();
        Assert.True(committedByA.Count < jobs, "node-a finished the whole burst before it was stopped.");

        await using var b = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "node-b",
            CreateTables = false,
            Scheduler = scheduler,
        });
        // Work node-b picks up after its due time finishes as DueDone.
        await WaitForStatusAsync(b, ids, Succeeded);

        // Work node-a abandoned may run again (at-least-once); work it committed must not.
        Assert.All(committedByA, id => Assert.DoesNotContain("node-b", completedOn[id]));
        Assert.All(ids, id => Assert.True(completedOn.ContainsKey(id), $"{id} finished without running."));
        var ownerB = b.ExecutionOwnerId();
        var rows = await RowsAsync(b, ids);
        Assert.All(rows.Where(x => committedByA.Contains(x.Id)), row => Assert.Equal(ownerA, row.LockHolder));
        Assert.All(rows, row => Assert.Contains(row.LockHolder, new[] { ownerA, ownerB }));
    }

    [PostgreSqlRaceFact]
    public async Task MixedBurst_OfSuccessesFailuresRetriesAndChains_ReachesEveryTerminalStateWithinTheConnectionBound()
    {
        const int maxConcurrency = 4;
        var connectionString = await server.CreateDatabaseAsync();
        var ok = Unique("ok");
        var failing = Unique("failing");
        var parent = Unique("parent");
        var child = Unique("child");
        var attempts = new ConcurrentDictionary<Guid, int>();

        await using var host = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "mixed",
            Scheduler = scheduler => scheduler.MaxConcurrency = maxConcurrency,
            Services = services =>
            {
                services.MapTicker(ok, (_, _) => Task.CompletedTask);
                services.MapTicker(failing, (context, _) =>
                {
                    attempts.AddOrUpdate(context.Id, 1, (_, count) => count + 1);
                    throw new InvalidOperationException("always fails");
                });
                services.MapTicker(parent, (_, _) => Task.CompletedTask);
                services.MapTicker(child, (_, _) => Task.CompletedTask);
            },
        });

        await using var connections = new ConnectionSampler(server, connectionString);
        var manager = host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var due = DateTime.UtcNow.AddSeconds(2);
        var succeeding = await AddDueAsync(host, ok, 150, due);
        var failingIds = (await manager.AddBatchAsync(Enumerable.Range(0, 50).Select(_ => new TimeTickerEntity
        {
            Function = failing,
            ExecutionTime = due,
            Retries = 1,
            RetryIntervals = [1],
        }).ToList())).EnsureSuccess().Result.Select(x => x.Id).ToList();
        var roots = new List<Guid>();
        for (var i = 0; i < 30; i++)
            roots.Add((await manager.AddChainAsync(c => c.RunAt(due).StartWith(parent).Then(child))).EnsureSuccess().Result.Id);

        await WaitForStatusAsync(host, succeeding, Succeeded);
        await WaitForStatusAsync(host, failingIds, TickerStatus.Failed);
        await WaitForStatusAsync(host, roots, Succeeded);
        var children = await host.QueryAsync(db => db.Set<TimeTickerEntity>().AsNoTracking()
            .Where(x => x.ParentId != null && roots.Contains(x.ParentId.Value)).Select(x => x.Id).ToListAsync());
        Assert.Equal(roots.Count, children.Count);
        await WaitForStatusAsync(host, children, Succeeded);
        var peak = await connections.StopAsync();

        Assert.All(failingIds, id => Assert.Equal(2, attempts[id]));
        Assert.All(await RowsAsync(host, failingIds), row => Assert.Equal(1, row.RetryCount));
        Assert.True(peak.Count <= maxConcurrency + 8, $"The host held {peak} for MaxConcurrency {maxConcurrency}.");
    }

    [PostgreSqlRaceFact]
    public async Task IdleNode_RunsAJobAddedForNow_WithoutWaitingForTheFallbackScan()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var function = Unique("wake");
        var ran = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await EndToEndHost.StartAsync(connectionString, new EndToEndHostOptions
        {
            Node = "idle",
            Services = services => services.MapTicker(function, (_, _) =>
            {
                ran.TrySetResult(DateTime.UtcNow);
                return Task.CompletedTask;
            }),
        });
        // Let the scheduler find nothing and go to sleep (the fallback scan is 30 seconds away).
        await Task.Delay(TimeSpan.FromSeconds(3));

        var added = DateTime.UtcNow;
        (await host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>()
            .AddAsync(new TimeTickerEntity { Function = function, ExecutionTime = added })).EnsureSuccess();
        var started = await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Adding the job wakes the sleeping loop; one polling interval (1s) is the most it may wait.
        Assert.True(started - added < TimeSpan.FromSeconds(3), $"The job started {(started - added).TotalMilliseconds:0} ms after it was added.");
    }

    private static string Unique(string name) => $"e2e.{name}.{Guid.NewGuid():N}";

    private static async Task<List<Guid>> AddDueAsync(IHost host, string function, int count, DateTime? due = null)
    {
        var executionTime = due ?? DateTime.UtcNow.AddSeconds(2);
        var result = await host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().AddBatchAsync(
            Enumerable.Range(0, count).Select(_ => new TimeTickerEntity { Function = function, ExecutionTime = executionTime }).ToList());
        return result.EnsureSuccess().Result.Select(x => x.Id).ToList();
    }

    private static Task<List<TimeTickerEntity>> RowsAsync(IHost host, IReadOnlyCollection<Guid> ids)
        => host.QueryAsync(db => db.Set<TimeTickerEntity>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync());

    /// <summary>The statuses of a job that ran to success: on time, or after its due time.</summary>
    private static readonly TickerStatus[] Succeeded = [TickerStatus.Done, TickerStatus.DueDone];

    private static Task WaitForStatusAsync(IHost host, IReadOnlyCollection<Guid> ids, params TickerStatus[] statuses)
        => EndToEndHost.WaitUntilAsync(async () =>
        {
            var rows = await RowsAsync(host, ids);
            return rows.Count == ids.Count && rows.All(x => statuses.Contains(x.Status));
        }, Deadline, $"{ids.Count} jobs to reach {string.Join(" or ", statuses)}", async () =>
        {
            var rows = await RowsAsync(host, ids);
            return "Still: " + string.Join(", ", rows.Where(x => !statuses.Contains(x.Status))
                .GroupBy(x => (x.Status, x.LockHolder, Leased: x.LeaseUntil != null))
                .Select(g => $"{g.Count()} {g.Key.Status} held by {g.Key.LockHolder ?? "nobody"}{(g.Key.Leased ? " (leased)" : "")}"));
        });

    /// <summary>Samples the hosts' open connections until stopped and reports the busiest sample.</summary>
    private sealed class ConnectionSampler : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task<Peak> _sampling;

        public ConnectionSampler(PostgreSqlEndToEndServer server, string connectionString)
            => _sampling = Task.Run(async () =>
            {
                var open = 0;
                List<string> busiest = [];
                while (!_stop.IsCancellationRequested)
                {
                    var connections = await server.HostConnectionsAsync(connectionString);
                    open = Math.Max(open, connections.Count);
                    var busy = connections.Where(x => !x.StartsWith("idle:", StringComparison.Ordinal)).ToList();
                    if (busy.Count > busiest.Count) busiest = busy;
                    await Task.Delay(10);
                }
                return new Peak(open, busiest);
            });

        public async Task<Peak> StopAsync()
        {
            _stop.Cancel();
            return await _sampling;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_stop.IsCancellationRequested) await StopAsync();
            _stop.Dispose();
        }
    }

    /// <summary>The most connections open at once, and the busiest moment's statements.</summary>
    private sealed record Peak(int Count, List<string> Busiest)
    {
        public override string ToString()
            => $"{Count} connections open at once; at the busiest moment {Busiest.Count} were working:" +
               $"{Environment.NewLine}{string.Join(Environment.NewLine, Busiest.Order())}";
    }
}
