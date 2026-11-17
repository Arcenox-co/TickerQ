using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal abstract partial class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker> : ITickerPromiseStateStore
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<IReadOnlyList<TickerPromiseStateRow>> ListPromiseStatesAsync(CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
        var rows = await session.Context.Set<TickerQPromiseEntity>().AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToPromiseRow).ToArray();
    }

    public async Task<TickerPromiseStateRow> GetPromiseStateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
        var row = await session.Context.Set<TickerQPromiseEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row == null ? null : ToPromiseRow(row);
    }

    public async Task<TickerPromiseStateRow> CreatePromiseStateAsync(TickerPromiseStateRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var entity = new TickerQPromiseEntity
        {
            Id = row.Id,
            ApplicationNamespaceKey = _runtimePartitionKey,
            Name = row.Name,
            StateJson = row.StateJson,
            CreatedAtUtc = row.CreatedAtUtc == default ? now : row.CreatedAtUtc,
            UpdatedAtUtc = now,
            Version = 1,
        };
        session.Context.Set<TickerQPromiseEntity>().Add(entity);
        try
        {
            await session.Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException($"A promise state row with id '{row.Id}' already exists.", ex);
        }
        return ToPromiseRow(entity);
    }

    public async Task<TickerPromiseStateRow> UpdatePromiseStateAsync(TickerPromiseStateRow row, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var changed = await session.Context.Set<TickerQPromiseEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == row.Id && x.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Name, row.Name)
                .SetProperty(x => x.StateJson, row.StateJson)
                .SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.Version, expectedVersion + 1), cancellationToken).ConfigureAwait(false);
        if (changed != 1)
            return null;
        var next = row.Copy();
        next.ApplicationNamespaceKey = _runtimePartitionKey;
        next.UpdatedAtUtc = now;
        next.Version = expectedVersion + 1;
        return next;
    }

    private static TickerPromiseStateRow ToPromiseRow(TickerQPromiseEntity row) => new()
    {
        Id = row.Id, ApplicationNamespaceKey = row.ApplicationNamespaceKey, Name = row.Name, StateJson = row.StateJson,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc, Version = row.Version,
    };
}
