using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class ExecutionLogPostgreSqlTests
{
    [PostgreSqlRaceFact]
    public async Task Relational_log_contract_and_concurrent_first_writes_use_the_newest_snapshot()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ExecutionLogPersistenceTests.LogContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var context = new ExecutionLogPersistenceTests.LogContext(options))
            await context.Database.EnsureCreatedAsync();
        await using var services = new ServiceCollection().AddSingleton<IDbContextFactory<ExecutionLogPersistenceTests.LogContext>>(
            new PooledDbContextFactory<ExecutionLogPersistenceTests.LogContext>(options)).BuildServiceProvider();
        await new Contract(services).Compressed_payload_survives_store_recreation_and_older_retries_cannot_replace_it();
        await new Contract(services).Cleanup_expires_and_caps_tails_without_touching_another_application();
        await new Contract(services).Retrying_the_same_flush_is_idempotent();
        await new Contract(services).Batch_read_returns_only_requested_unexpired_records_in_the_exact_partition();
        var store = new EfExecutionLogPersistence<ExecutionLogPersistenceTests.LogContext>(services);
        var id = Guid.NewGuid(); var now = DateTime.UtcNow.Ticks;
        var rows = Enumerable.Range(0, 12).Select(i => new ExecutionLogArchive
        {
            Id = ExecutionLogArchive.StorageId("concurrent", id), PartitionKey = "concurrent", TickerId = id,
            UpdatedAtUtcTicks = now + i, ExpiresAtUtcTicks = now + TimeSpan.TicksPerDay, Payload = [(byte)i]
        }).ToArray();
        await Task.WhenAll(rows.Select(row => store.WriteAsync([row], CancellationToken.None)));
        Assert.Equal(rows[^1].Payload, (await store.ReadAsync("concurrent", id, now, CancellationToken.None)).Payload);
    }

    private sealed class Contract(IServiceProvider services) : ExecutionLogPersistenceContract
    {
        protected override ITickerExecutionLogPersistence NewStore() => new EfExecutionLogPersistence<ExecutionLogPersistenceTests.LogContext>(services);
    }
}
