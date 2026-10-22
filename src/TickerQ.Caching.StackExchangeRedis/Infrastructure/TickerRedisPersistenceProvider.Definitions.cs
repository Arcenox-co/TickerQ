using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<IReadOnlyList<SavedTickerDefinition>> ListDefinitionsAsync(
        SavedTickerDefinitionKind? kind = null, SavedTickerDefinitionTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await Serializer.LoadAllFromSetAsync<SavedTickerDefinition>(
            Keys.DefinitionIds, Keys.Definition, cancellationToken,
            x => (!kind.HasValue || x.Kind == kind) && (!target.HasValue || x.Target == target))
            .ConfigureAwait(false);
        return rows.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray();
    }

    public Task<SavedTickerDefinition> GetDefinitionAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Serializer.GetAsync<SavedTickerDefinition>(Keys.Definition(id));
    }

    public async Task<SavedTickerDefinition> CreateDefinitionAsync(SavedTickerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        var now = Clock.UtcNow;
        var row = definition.Copy();
        row.ApplicationNamespaceKey = Keys.Partition.StorageKey;
        SavedTickerDefinitionNames.AssignKey(row);
        row.CreatedAtUtc = row.CreatedAtUtc == default ? now : row.CreatedAtUtc;
        row.UpdatedAtUtc = now;
        row.Version = 1;
        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.KeyNotExists(Keys.Definition(row.Id)));
        transaction.AddCondition(Condition.KeyNotExists(Keys.DefinitionName(row.NameKey)));
        _ = transaction.StringSetAsync(Keys.Definition(row.Id), Serializer.Serialize(row));
        _ = transaction.StringSetAsync(Keys.DefinitionName(row.NameKey), row.Id.ToString());
        _ = transaction.SetAddAsync(Keys.DefinitionIds, row.Id.ToString());
        if (!await transaction.ExecuteAsync().ConfigureAwait(false))
        {
            if (await Db.KeyExistsAsync(Keys.DefinitionName(row.NameKey)).ConfigureAwait(false))
                throw new DuplicateSavedTickerDefinitionNameException(row.Name);
            throw new InvalidOperationException($"A saved definition with id '{row.Id}' already exists.");
        }
        return row;
    }

    public async Task<SavedTickerDefinition> UpdateDefinitionAsync(SavedTickerDefinition definition,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var current = await GetDefinitionAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.Version != expectedVersion || current.ActivatedRootTickerId.HasValue ||
            current.ActivationToken.HasValue) return null;
        var next = definition.Copy();
        next.ApplicationNamespaceKey = Keys.Partition.StorageKey;
        SavedTickerDefinitionNames.AssignKey(next);
        next.CreatedAtUtc = current.CreatedAtUtc;
        next.UpdatedAtUtc = Clock.UtcNow;
        next.Version = expectedVersion + 1;
        next.ActivationToken = null;
        next.PendingRootTickerId = null;
        next.ActivationClaimedAtUtc = null;
        next.ActivatedRootTickerId = null;
        next.ActivatedAtUtc = null;
        if (await ReplaceDefinitionAsync(current, next, cancellationToken).ConfigureAwait(false)) return next;
        var owner = await Db.StringGetAsync(Keys.DefinitionName(next.NameKey)).ConfigureAwait(false);
        if (owner.HasValue && !string.Equals(owner.ToString(), next.Id.ToString(), StringComparison.Ordinal))
            throw new DuplicateSavedTickerDefinitionNameException(next.Name);
        return null;
    }

    public async Task<bool> DeleteDefinitionAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetDefinitionAsync(id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.Version != expectedVersion) return false;
        var key = Keys.Definition(id);
        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(key, Serializer.Serialize(current)));
        transaction.AddCondition(Condition.StringEqual(Keys.DefinitionName(current.NameKey), id.ToString()));
        _ = transaction.KeyDeleteAsync(key);
        _ = transaction.KeyDeleteAsync(Keys.DefinitionName(current.NameKey));
        _ = transaction.SetRemoveAsync(Keys.DefinitionIds, id.ToString());
        return await transaction.ExecuteAsync().ConfigureAwait(false);
    }

    public async Task<SavedTickerDefinition> TryClaimDraftActivationAsync(Guid id, long expectedVersion,
        Guid activationToken, Guid pendingRootTickerId, DateTime claimedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var current = await GetDefinitionAsync(id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.Version != expectedVersion ||
            current.Kind != SavedTickerDefinitionKind.Draft || current.Target != SavedTickerDefinitionTarget.TimeTicker ||
            current.ActivatedRootTickerId.HasValue || current.ActivationToken.HasValue) return null;
        var next = current.Copy();
        next.ActivationToken = activationToken;
        next.PendingRootTickerId = pendingRootTickerId;
        next.ActivationClaimedAtUtc = claimedAtUtc;
        next.UpdatedAtUtc = claimedAtUtc;
        next.Version++;
        return await ReplaceDefinitionAsync(current, next, cancellationToken).ConfigureAwait(false) ? next : null;
    }

    public async Task<bool> CompleteDraftActivationAsync(Guid id, Guid activationToken,
        Guid rootTickerId, DateTime activatedAtUtc, CancellationToken cancellationToken = default)
    {
        var current = await GetDefinitionAsync(id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.ActivationToken != activationToken ||
            current.PendingRootTickerId != rootTickerId || current.ActivatedRootTickerId.HasValue) return false;
        var next = current.Copy();
        next.ActivatedRootTickerId = rootTickerId;
        next.ActivatedAtUtc = activatedAtUtc;
        next.ActivationToken = null;
        next.UpdatedAtUtc = activatedAtUtc;
        next.Version++;
        return await ReplaceDefinitionAsync(current, next, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ReleaseDraftActivationAsync(Guid id, Guid activationToken,
        CancellationToken cancellationToken = default)
    {
        var current = await GetDefinitionAsync(id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.ActivationToken != activationToken || current.ActivatedRootTickerId.HasValue)
            return false;
        var next = current.Copy();
        next.ActivationToken = null;
        next.PendingRootTickerId = null;
        next.ActivationClaimedAtUtc = null;
        next.UpdatedAtUtc = Clock.UtcNow;
        next.Version++;
        return await ReplaceDefinitionAsync(current, next, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ReplaceDefinitionAsync(SavedTickerDefinition current,
        SavedTickerDefinition next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Keys.Definition(current.Id);
        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(key, Serializer.Serialize(current)));
        transaction.AddCondition(Condition.StringEqual(
            Keys.DefinitionName(current.NameKey), current.Id.ToString()));
        if (!string.Equals(current.NameKey, next.NameKey, StringComparison.Ordinal))
            transaction.AddCondition(Condition.KeyNotExists(Keys.DefinitionName(next.NameKey)));
        _ = transaction.StringSetAsync(key, Serializer.Serialize(next));
        _ = transaction.StringSetAsync(Keys.DefinitionName(next.NameKey), next.Id.ToString());
        if (!string.Equals(current.NameKey, next.NameKey, StringComparison.Ordinal))
            _ = transaction.KeyDeleteAsync(Keys.DefinitionName(current.NameKey));
        return await transaction.ExecuteAsync().ConfigureAwait(false);
    }
}
