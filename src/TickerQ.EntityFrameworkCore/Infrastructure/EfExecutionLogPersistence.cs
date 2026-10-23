using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal sealed class EfExecutionLogPersistence<TContext>(IServiceProvider services) : ITickerExecutionLogPersistence where TContext : DbContext
{
    public async Task WriteAsync(IReadOnlyList<ExecutionLogArchive> records, CancellationToken cancellationToken)
    {
        using var lease = await DbContextLease<TContext>.CreateAsync(services, cancellationToken);
        var db = lease.Context;
        foreach (var row in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await UpdateAsync(db, row, cancellationToken) > 0) continue;
            if (await db.Set<ExecutionLogArchive>().AnyAsync(x => x.Id == row.Id, cancellationToken)) continue;
            db.Add(row);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                db.Entry(row).State = EntityState.Detached;
                // Another node may have inserted this tail. Never replace a newer snapshot.
                if (!await db.Set<ExecutionLogArchive>().AnyAsync(x => x.Id == row.Id, cancellationToken)) throw;
                await UpdateAsync(db, row, cancellationToken);
            }
            db.Entry(row).State = EntityState.Detached;
        }
    }

    private static Task<int> UpdateAsync(DbContext db, ExecutionLogArchive row, CancellationToken ct) =>
        db.Set<ExecutionLogArchive>().Where(x => x.Id == row.Id && x.PartitionKey == row.PartitionKey && x.UpdatedAtUtcTicks <= row.UpdatedAtUtcTicks)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Payload, row.Payload)
                .SetProperty(x => x.UpdatedAtUtcTicks, row.UpdatedAtUtcTicks)
                .SetProperty(x => x.ExpiresAtUtcTicks, row.ExpiresAtUtcTicks)
                .SetProperty(x => x.Truncated, row.Truncated), ct);

    public async Task<ExecutionLogArchive> ReadAsync(string partitionKey, Guid tickerId, long nowUtcTicks, CancellationToken cancellationToken)
    {
        using var lease = await DbContextLease<TContext>.CreateAsync(services, cancellationToken);
        var id = ExecutionLogArchive.StorageId(partitionKey, tickerId);
        return await lease.Context.Set<ExecutionLogArchive>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.PartitionKey == partitionKey && x.ExpiresAtUtcTicks > nowUtcTicks, cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionLogArchive>> ReadBatchAsync(string partitionKey, IReadOnlyList<Guid> tickerIds,
        long nowUtcTicks, CancellationToken cancellationToken)
    {
        using var lease = await DbContextLease<TContext>.CreateAsync(services, cancellationToken);
        var ids = tickerIds.Select(id => ExecutionLogArchive.StorageId(partitionKey, id)).ToArray();
        return await lease.Context.Set<ExecutionLogArchive>().AsNoTracking()
            .Where(x => x.PartitionKey == partitionKey && x.ExpiresAtUtcTicks > nowUtcTicks)
            .Where(EfPredicateBuilder.Contains<ExecutionLogArchive, string>(ids, x => x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task PruneAsync(string partitionKey, long nowUtcTicks, int maxExecutions, int batchSize, CancellationToken cancellationToken)
    {
        using var lease = await DbContextLease<TContext>.CreateAsync(services, cancellationToken);
        var rows = lease.Context.Set<ExecutionLogArchive>();
        var expired = await rows.Where(x => x.PartitionKey == partitionKey && x.ExpiresAtUtcTicks <= nowUtcTicks)
            .OrderBy(x => x.ExpiresAtUtcTicks).Select(x => x.Id).Take(batchSize).ToArrayAsync(cancellationToken);
        // Re-check expiry because an active writer could have refreshed the selected row.
        if (expired.Length > 0) await rows
            .Where(x => x.PartitionKey == partitionKey && x.ExpiresAtUtcTicks <= nowUtcTicks)
            .Where(EfPredicateBuilder.Contains<ExecutionLogArchive, string>(expired, x => x.Id))
            .ExecuteDeleteAsync(cancellationToken);
        var excess = await rows.Where(x => x.PartitionKey == partitionKey)
            .OrderByDescending(x => x.UpdatedAtUtcTicks).ThenByDescending(x => x.Id)
            .Skip(maxExecutions).Take(batchSize).Select(x => new { x.Id, x.UpdatedAtUtcTicks }).ToArrayAsync(cancellationToken);
        foreach (var item in excess)
            await rows.Where(x => x.Id == item.Id && x.PartitionKey == partitionKey && x.UpdatedAtUtcTicks == item.UpdatedAtUtcTicks)
                .ExecuteDeleteAsync(cancellationToken);
    }
}
