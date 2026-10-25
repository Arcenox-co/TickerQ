using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal abstract partial class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<IReadOnlyList<SchedulePolicy>> ListSchedulePoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var rows = await session.Context.Set<SchedulePolicyEntity>().AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToPolicy).ToArray();
    }

    public async Task<SchedulePolicy> GetSchedulePolicyAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var row = await session.Context.Set<SchedulePolicyEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id,
                cancellationToken).ConfigureAwait(false);
        return row == null ? null : ToPolicy(row);
    }

    public async Task<SchedulePolicy> GetSchedulePolicyByNameAsync(string name,
        CancellationToken cancellationToken = default)
    {
        var key = SchedulePolicyNames.CreateKey(name);
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var row = await session.Context.Set<SchedulePolicyEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.NameKey == key,
                cancellationToken).ConfigureAwait(false);
        return row == null ? null : ToPolicy(row);
    }

    public async Task<SchedulePolicy> CreateSchedulePolicyAsync(SchedulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var row = ToEntity(policy);
        row.Id = row.Id == Guid.Empty ? Guid.NewGuid() : row.Id;
        row.ApplicationNamespaceKey = _runtimePartitionKey;
        row.NameKey = SchedulePolicyNames.CreateKey(row.Name);
        row.CreatedAtUtc = row.CreatedAtUtc == default ? _clock.UtcNow : row.CreatedAtUtc;
        row.UpdatedAtUtc = _clock.UtcNow;
        row.Version = 1;
        session.Context.Add(row);
        try { await session.Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateException)
        {
            if (await PolicyNameExistsAsync(session.Context, row.NameKey, row.Id, cancellationToken)
                    .ConfigureAwait(false))
                throw new DuplicateSchedulePolicyNameException(row.Name);
            throw;
        }
        return ToPolicy(row);
    }

    public async Task<SchedulePolicy> UpdateSchedulePolicyAsync(SchedulePolicy policy, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var nameKey = SchedulePolicyNames.CreateKey(policy.Name);
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        int changed;
        try
        {
            changed = await session.Context.Set<SchedulePolicyEntity>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == policy.Id &&
                            x.Version == expectedVersion)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Name, policy.Name)
                    .SetProperty(x => x.NameKey, nameKey)
                    .SetProperty(x => x.Description, policy.Description)
                    .SetProperty(x => x.DefinitionJson, SchedulePolicySerializer.Serialize(policy))
                    .SetProperty(x => x.UpdatedAtUtc, _clock.UtcNow)
                    .SetProperty(x => x.Version, expectedVersion + 1), cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (await PolicyNameExistsAsync(session.Context, nameKey, policy.Id, cancellationToken)
                    .ConfigureAwait(false))
                throw new DuplicateSchedulePolicyNameException(policy.Name);
            throw;
        }
        return changed == 1 ? await GetSchedulePolicyAsync(policy.Id, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<bool> DeleteSchedulePolicyAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        return await session.Context.Set<SchedulePolicyEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id && x.Version == expectedVersion)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> IsSchedulePolicyInUseAsync(string name,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        return await session.Context.Set<TTimeTicker>().AsNoTracking().ExistsAsync(x =>
                   x.ApplicationNamespaceKey == _runtimePartitionKey && x.SchedulePolicyName == normalized,
                   cancellationToken).ConfigureAwait(false) ||
               await session.Context.Set<TCronTicker>().AsNoTracking().ExistsAsync(x =>
                   x.ApplicationNamespaceKey == _runtimePartitionKey && x.SchedulePolicyName == normalized,
                   cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PolicyNameExistsAsync(TDbContext context, string nameKey, Guid id,
        CancellationToken cancellationToken) => await context.Set<SchedulePolicyEntity>().AsNoTracking()
        .ExistsAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                       x.NameKey == nameKey && x.Id != id, cancellationToken).ConfigureAwait(false);

    private static SchedulePolicy ToPolicy(SchedulePolicyEntity row)
    {
        var policy = SchedulePolicySerializer.Deserialize(row.DefinitionJson) ?? new SchedulePolicy();
        policy.Id = row.Id; policy.ApplicationNamespaceKey = row.ApplicationNamespaceKey;
        policy.Name = row.Name; policy.NameKey = row.NameKey; policy.Description = row.Description;
        policy.CreatedAtUtc = row.CreatedAtUtc; policy.UpdatedAtUtc = row.UpdatedAtUtc; policy.Version = row.Version;
        return policy;
    }

    private static SchedulePolicyEntity ToEntity(SchedulePolicy policy) => new()
    {
        Id = policy.Id, ApplicationNamespaceKey = policy.ApplicationNamespaceKey,
        Name = policy.Name, NameKey = policy.NameKey, Description = policy.Description,
        DefinitionJson = SchedulePolicySerializer.Serialize(policy), CreatedAtUtc = policy.CreatedAtUtc,
        UpdatedAtUtc = policy.UpdatedAtUtc, Version = policy.Version,
    };
}
