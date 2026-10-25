using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal abstract partial class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<IReadOnlyList<SavedTickerDefinition>> ListDefinitionsAsync(
        SavedTickerDefinitionKind? kind = null, SavedTickerDefinitionTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var query = session.Context.Set<TickerQDefinitionEntity>().AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey);
        if (kind.HasValue) query = query.Where(x => x.Kind == kind.Value);
        if (target.HasValue) query = query.Where(x => x.Target == target.Value);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDefinition).ToArray();
    }

    public async Task<SavedTickerDefinition> GetDefinitionAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var row = await session.Context.Set<TickerQDefinitionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id,
                cancellationToken).ConfigureAwait(false);
        return row == null ? null : ToDefinition(row);
    }

    public async Task<SavedTickerDefinition> CreateDefinitionAsync(SavedTickerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var now = _clock.UtcNow;
        var row = ToEntity(definition);
        row.ApplicationNamespaceKey = _runtimePartitionKey;
        row.NameKey = SavedTickerDefinitionNames.CreateKey(row.Kind, row.Target, row.Name, row.Id);
        await ThrowIfDefinitionNameExistsAsync(session.Context, row, null, cancellationToken).ConfigureAwait(false);
        row.CreatedAtUtc = row.CreatedAtUtc == default ? now : row.CreatedAtUtc;
        row.UpdatedAtUtc = now;
        row.Version = 1;
        session.Context.Set<TickerQDefinitionEntity>().Add(row);
        try
        {
            await session.Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await ThrowIfDefinitionNameExistsAsync(session.Context, row, null, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        return ToDefinition(row);
    }

    public async Task<SavedTickerDefinition> UpdateDefinitionAsync(SavedTickerDefinition definition,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var now = _clock.UtcNow;
        var nameKey = SavedTickerDefinitionNames.CreateKey(
            definition.Kind, definition.Target, definition.Name, definition.Id);
        await ThrowIfDefinitionNameExistsAsync(session.Context, new TickerQDefinitionEntity
        {
            Id = definition.Id,
            Kind = definition.Kind,
            Target = definition.Target,
            Name = definition.Name,
            NameKey = nameKey,
        }, definition.Id, cancellationToken).ConfigureAwait(false);
        int changed;
        try
        {
            changed = await session.Context.Set<TickerQDefinitionEntity>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == definition.Id &&
                            x.Version == expectedVersion && x.ActivatedRootTickerId == null && x.ActivationToken == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Kind, definition.Kind)
                    .SetProperty(x => x.Target, definition.Target)
                    .SetProperty(x => x.Name, definition.Name)
                    .SetProperty(x => x.NameKey, nameKey)
                    .SetProperty(x => x.Description, definition.Description)
                    .SetProperty(x => x.DefinitionJson, definition.DefinitionJson)
                    .SetProperty(x => x.IncludesRequestData, definition.IncludesRequestData)
                    .SetProperty(x => x.SchemaVersion, definition.SchemaVersion)
                    .SetProperty(x => x.UpdatedAtUtc, now)
                    .SetProperty(x => x.Version, expectedVersion + 1), cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await ThrowIfDefinitionNameExistsAsync(session.Context, new TickerQDefinitionEntity
            {
                Id = definition.Id,
                Kind = definition.Kind,
                Target = definition.Target,
                Name = definition.Name,
                NameKey = nameKey,
            }, definition.Id, cancellationToken).ConfigureAwait(false);
            throw;
        }
        return changed == 1 ? await GetDefinitionAsync(definition.Id, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<bool> DeleteDefinitionAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        return await session.Context.Set<TickerQDefinitionEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id && x.Version == expectedVersion)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<SavedTickerDefinition> TryClaimDraftActivationAsync(Guid id, long expectedVersion,
        Guid activationToken, Guid pendingRootTickerId, DateTime claimedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        var changed = await session.Context.Set<TickerQDefinitionEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id &&
                        x.Version == expectedVersion && x.Kind == SavedTickerDefinitionKind.Draft &&
                        x.Target == SavedTickerDefinitionTarget.TimeTicker && x.ActivatedRootTickerId == null &&
                        x.ActivationToken == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ActivationToken, activationToken)
                .SetProperty(x => x.PendingRootTickerId, pendingRootTickerId)
                .SetProperty(x => x.ActivationClaimedAtUtc, claimedAtUtc)
                .SetProperty(x => x.UpdatedAtUtc, claimedAtUtc)
                .SetProperty(x => x.Version, expectedVersion + 1), cancellationToken).ConfigureAwait(false);
        return changed == 1 ? await GetDefinitionAsync(id, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<bool> CompleteDraftActivationAsync(Guid id, Guid activationToken,
        Guid rootTickerId, DateTime activatedAtUtc, CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        return await session.Context.Set<TickerQDefinitionEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id &&
                        x.ActivationToken == activationToken && x.PendingRootTickerId == rootTickerId &&
                        x.ActivatedRootTickerId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ActivatedRootTickerId, rootTickerId)
                .SetProperty(x => x.ActivatedAtUtc, activatedAtUtc)
                .SetProperty(x => x.ActivationToken, (Guid?)null)
                .SetProperty(x => x.UpdatedAtUtc, activatedAtUtc)
                .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ReleaseDraftActivationAsync(Guid id, Guid activationToken,
        CancellationToken cancellationToken = default)
    {
        using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        return await session.Context.Set<TickerQDefinitionEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id &&
                        x.ActivationToken == activationToken && x.ActivatedRootTickerId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ActivationToken, (Guid?)null)
                .SetProperty(x => x.PendingRootTickerId, (Guid?)null)
                .SetProperty(x => x.ActivationClaimedAtUtc, (DateTime?)null)
                .SetProperty(x => x.UpdatedAtUtc, _clock.UtcNow)
                .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken).ConfigureAwait(false) == 1;
    }

    private static SavedTickerDefinition ToDefinition(TickerQDefinitionEntity row) => new()
    {
        Id = row.Id, ApplicationNamespaceKey = row.ApplicationNamespaceKey, Kind = row.Kind,
        Target = row.Target, Name = row.Name, NameKey = row.NameKey, Description = row.Description,
        DefinitionJson = row.DefinitionJson, IncludesRequestData = row.IncludesRequestData,
        SchemaVersion = row.SchemaVersion, CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
        Version = row.Version, ActivationToken = row.ActivationToken,
        PendingRootTickerId = row.PendingRootTickerId, ActivationClaimedAtUtc = row.ActivationClaimedAtUtc,
        ActivatedRootTickerId = row.ActivatedRootTickerId, ActivatedAtUtc = row.ActivatedAtUtc,
    };

    private static TickerQDefinitionEntity ToEntity(SavedTickerDefinition row) => new()
    {
        Id = row.Id, ApplicationNamespaceKey = row.ApplicationNamespaceKey, Kind = row.Kind,
        Target = row.Target, Name = row.Name, NameKey = row.NameKey, Description = row.Description,
        DefinitionJson = row.DefinitionJson, IncludesRequestData = row.IncludesRequestData,
        SchemaVersion = row.SchemaVersion, CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
        Version = row.Version, ActivationToken = row.ActivationToken,
        PendingRootTickerId = row.PendingRootTickerId, ActivationClaimedAtUtc = row.ActivationClaimedAtUtc,
        ActivatedRootTickerId = row.ActivatedRootTickerId, ActivatedAtUtc = row.ActivatedAtUtc,
    };

    private async Task ThrowIfDefinitionNameExistsAsync(TDbContext context, TickerQDefinitionEntity candidate,
        Guid? excludedId, CancellationToken cancellationToken)
    {
        if (candidate.Kind != SavedTickerDefinitionKind.Template) return;
        var exists = await context.Set<TickerQDefinitionEntity>().AsNoTracking().ExistsAsync(row =>
            row.ApplicationNamespaceKey == _runtimePartitionKey && row.NameKey == candidate.NameKey &&
            (!excludedId.HasValue || row.Id != excludedId.Value), cancellationToken).ConfigureAwait(false);
        if (exists) throw new DuplicateSavedTickerDefinitionNameException(candidate.Name);
    }
}
