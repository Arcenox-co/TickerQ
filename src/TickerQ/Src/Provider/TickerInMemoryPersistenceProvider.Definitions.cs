using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
{
    public Task<IReadOnlyList<SavedTickerDefinition>> ListDefinitionsAsync(
        SavedTickerDefinitionKind? kind = null, SavedTickerDefinitionTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
        {
            IReadOnlyList<SavedTickerDefinition> result = _partitionState.Definitions.Values
                .Where(x => (!kind.HasValue || x.Kind == kind) && (!target.HasValue || x.Target == target))
                .OrderByDescending(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => x.Copy())
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<SavedTickerDefinition> GetDefinitionAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
            return Task.FromResult(_partitionState.Definitions.TryGetValue(id, out var row) ? row.Copy() : null);
    }

    public Task<SavedTickerDefinition> CreateDefinitionAsync(SavedTickerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
        {
            if (_partitionState.Definitions.ContainsKey(definition.Id))
                throw new InvalidOperationException($"A saved definition with id '{definition.Id}' already exists.");
            var now = _clock.UtcNow;
            var row = definition.Copy();
            row.ApplicationNamespaceKey = _runtimePartitionKey;
            SavedTickerDefinitionNames.AssignKey(row);
            ThrowIfDefinitionNameExists(row);
            row.CreatedAtUtc = row.CreatedAtUtc == default ? now : row.CreatedAtUtc;
            row.UpdatedAtUtc = now;
            row.Version = 1;
            _partitionState.Definitions.Add(row.Id, row);
            return Task.FromResult(row.Copy());
        }
    }

    public Task<SavedTickerDefinition> UpdateDefinitionAsync(SavedTickerDefinition definition,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
        {
            if (!_partitionState.Definitions.TryGetValue(definition.Id, out var current) ||
                current.Version != expectedVersion || current.ActivatedRootTickerId.HasValue ||
                current.ActivationToken.HasValue)
                return Task.FromResult<SavedTickerDefinition>(null);
            var row = definition.Copy();
            row.ApplicationNamespaceKey = _runtimePartitionKey;
            SavedTickerDefinitionNames.AssignKey(row);
            ThrowIfDefinitionNameExists(row, row.Id);
            row.CreatedAtUtc = current.CreatedAtUtc;
            row.UpdatedAtUtc = _clock.UtcNow;
            row.Version = current.Version + 1;
            row.ActivationToken = null;
            row.PendingRootTickerId = null;
            row.ActivationClaimedAtUtc = null;
            row.ActivatedRootTickerId = null;
            row.ActivatedAtUtc = null;
            _partitionState.Definitions[row.Id] = row;
            return Task.FromResult(row.Copy());
        }
    }

    public Task<bool> DeleteDefinitionAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
        {
            if (!_partitionState.Definitions.TryGetValue(id, out var row) || row.Version != expectedVersion)
                return Task.FromResult(false);
            return Task.FromResult(_partitionState.Definitions.Remove(id));
        }
    }

    public Task<SavedTickerDefinition> TryClaimDraftActivationAsync(Guid id, long expectedVersion,
        Guid activationToken, Guid pendingRootTickerId, DateTime claimedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
        {
            if (!_partitionState.Definitions.TryGetValue(id, out var row) || row.Version != expectedVersion ||
                row.Kind != SavedTickerDefinitionKind.Draft || row.Target != SavedTickerDefinitionTarget.TimeTicker ||
                row.ActivatedRootTickerId.HasValue || row.ActivationToken.HasValue)
                return Task.FromResult<SavedTickerDefinition>(null);
            row.ActivationToken = activationToken;
            row.PendingRootTickerId = pendingRootTickerId;
            row.ActivationClaimedAtUtc = claimedAtUtc;
            row.UpdatedAtUtc = claimedAtUtc;
            row.Version++;
            return Task.FromResult(row.Copy());
        }
    }

    public Task<bool> CompleteDraftActivationAsync(Guid id, Guid activationToken,
        Guid rootTickerId, DateTime activatedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
        {
            if (!_partitionState.Definitions.TryGetValue(id, out var row) || row.ActivationToken != activationToken ||
                row.PendingRootTickerId != rootTickerId || row.ActivatedRootTickerId.HasValue)
                return Task.FromResult(false);
            row.ActivatedRootTickerId = rootTickerId;
            row.ActivatedAtUtc = activatedAtUtc;
            row.ActivationToken = null;
            row.UpdatedAtUtc = activatedAtUtc;
            row.Version++;
            return Task.FromResult(true);
        }
    }

    public Task<bool> ReleaseDraftActivationAsync(Guid id, Guid activationToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.DefinitionsLock)
        {
            if (!_partitionState.Definitions.TryGetValue(id, out var row) || row.ActivationToken != activationToken ||
                row.ActivatedRootTickerId.HasValue)
                return Task.FromResult(false);
            row.ActivationToken = null;
            row.PendingRootTickerId = null;
            row.ActivationClaimedAtUtc = null;
            row.UpdatedAtUtc = _clock.UtcNow;
            row.Version++;
            return Task.FromResult(true);
        }
    }

    private void ThrowIfDefinitionNameExists(SavedTickerDefinition candidate, Guid? excludedId = null)
    {
        if (candidate.Kind != SavedTickerDefinitionKind.Template) return;
        if (_partitionState.Definitions.Values.Any(row => row.Id != excludedId &&
                row.Kind == SavedTickerDefinitionKind.Template &&
                row.Target == candidate.Target &&
                string.Equals(
                    string.IsNullOrEmpty(row.NameKey)
                        ? SavedTickerDefinitionNames.CreateKey(row.Kind, row.Target, row.Name, row.Id)
                        : row.NameKey,
                    candidate.NameKey,
                    StringComparison.Ordinal)))
            throw new DuplicateSavedTickerDefinitionNameException(candidate.Name);
    }
}
