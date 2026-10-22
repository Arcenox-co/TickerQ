using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Infrastructure;

internal sealed partial class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private FilterDefinition<SavedTickerDefinition> DefinitionInPartition(Guid id)
        => Builders<SavedTickerDefinition>.Filter.And(
            Builders<SavedTickerDefinition>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
            Builders<SavedTickerDefinition>.Filter.Eq(x => x.Id, id));

    public async Task<IReadOnlyList<SavedTickerDefinition>> ListDefinitionsAsync(
        SavedTickerDefinitionKind? kind = null, SavedTickerDefinitionTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<SavedTickerDefinition>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey);
        if (kind.HasValue) filter &= Builders<SavedTickerDefinition>.Filter.Eq(x => x.Kind, kind.Value);
        if (target.HasValue) filter &= Builders<SavedTickerDefinition>.Filter.Eq(x => x.Target, target.Value);
        return await _context.Definitions.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<SavedTickerDefinition> GetDefinitionAsync(Guid id,
        CancellationToken cancellationToken = default)
        => _context.Definitions.Find(DefinitionInPartition(id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<SavedTickerDefinition> CreateDefinitionAsync(SavedTickerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var now = _clock.UtcNow;
        var row = definition.Copy();
        row.ApplicationNamespaceKey = _runtimePartitionKey;
        SavedTickerDefinitionNames.AssignKey(row);
        await ThrowIfDefinitionNameExistsAsync(row, null, cancellationToken).ConfigureAwait(false);
        row.CreatedAtUtc = row.CreatedAtUtc == default ? now : row.CreatedAtUtc;
        row.UpdatedAtUtc = now;
        row.Version = 1;
        try
        {
            await _context.Definitions.InsertOneAsync(row, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException exception) when (
            exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateSavedTickerDefinitionNameException(row.Name);
        }
        return row;
    }

    public async Task<SavedTickerDefinition> UpdateDefinitionAsync(SavedTickerDefinition definition,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var nameKey = SavedTickerDefinitionNames.CreateKey(
            definition.Kind, definition.Target, definition.Name, definition.Id);
        var candidate = definition.Copy();
        candidate.NameKey = nameKey;
        await ThrowIfDefinitionNameExistsAsync(candidate, definition.Id, cancellationToken).ConfigureAwait(false);
        var filter = DefinitionInPartition(definition.Id) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.Version, expectedVersion) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivatedRootTickerId, null) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivationToken, null);
        var update = Builders<SavedTickerDefinition>.Update
            .Set(x => x.Kind, definition.Kind).Set(x => x.Target, definition.Target)
            .Set(x => x.Name, definition.Name).Set(x => x.NameKey, nameKey)
            .Set(x => x.Description, definition.Description)
            .Set(x => x.DefinitionJson, definition.DefinitionJson)
            .Set(x => x.IncludesRequestData, definition.IncludesRequestData)
            .Set(x => x.SchemaVersion, definition.SchemaVersion).Set(x => x.UpdatedAtUtc, _clock.UtcNow)
            .Set(x => x.Version, expectedVersion + 1);
        try
        {
            return await _context.Definitions.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<SavedTickerDefinition> { ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false);
        }
        catch (MongoCommandException exception) when (exception.Code == 11000)
        {
            throw new DuplicateSavedTickerDefinitionNameException(definition.Name);
        }
    }

    public async Task<bool> DeleteDefinitionAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
        => (await _context.Definitions.DeleteOneAsync(
            DefinitionInPartition(id) & Builders<SavedTickerDefinition>.Filter.Eq(x => x.Version, expectedVersion),
            cancellationToken).ConfigureAwait(false)).DeletedCount == 1;

    public Task<SavedTickerDefinition> TryClaimDraftActivationAsync(Guid id, long expectedVersion,
        Guid activationToken, Guid pendingRootTickerId, DateTime claimedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var filter = DefinitionInPartition(id) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.Version, expectedVersion) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.Kind, SavedTickerDefinitionKind.Draft) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.Target, SavedTickerDefinitionTarget.TimeTicker) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivatedRootTickerId, null) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivationToken, null);
        var update = Builders<SavedTickerDefinition>.Update
            .Set(x => x.ActivationToken, activationToken).Set(x => x.PendingRootTickerId, pendingRootTickerId)
            .Set(x => x.ActivationClaimedAtUtc, claimedAtUtc).Set(x => x.UpdatedAtUtc, claimedAtUtc)
            .Set(x => x.Version, expectedVersion + 1);
        return _context.Definitions.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<SavedTickerDefinition> { ReturnDocument = ReturnDocument.After }, cancellationToken);
    }

    public async Task<bool> CompleteDraftActivationAsync(Guid id, Guid activationToken,
        Guid rootTickerId, DateTime activatedAtUtc, CancellationToken cancellationToken = default)
    {
        var filter = DefinitionInPartition(id) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivationToken, activationToken) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.PendingRootTickerId, rootTickerId) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivatedRootTickerId, null);
        var update = Builders<SavedTickerDefinition>.Update
            .Set(x => x.ActivatedRootTickerId, rootTickerId).Set(x => x.ActivatedAtUtc, activatedAtUtc)
            .Set(x => x.ActivationToken, null).Set(x => x.UpdatedAtUtc, activatedAtUtc).Inc(x => x.Version, 1);
        return (await _context.Definitions.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false)).ModifiedCount == 1;
    }

    public async Task<bool> ReleaseDraftActivationAsync(Guid id, Guid activationToken,
        CancellationToken cancellationToken = default)
    {
        var filter = DefinitionInPartition(id) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivationToken, activationToken) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.ActivatedRootTickerId, null);
        var update = Builders<SavedTickerDefinition>.Update
            .Set(x => x.ActivationToken, null).Set(x => x.PendingRootTickerId, null)
            .Set(x => x.ActivationClaimedAtUtc, null).Set(x => x.UpdatedAtUtc, _clock.UtcNow)
            .Inc(x => x.Version, 1);
        return (await _context.Definitions.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false)).ModifiedCount == 1;
    }

    private async Task ThrowIfDefinitionNameExistsAsync(SavedTickerDefinition candidate, Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (candidate.Kind != SavedTickerDefinitionKind.Template) return;
        var filter = Builders<SavedTickerDefinition>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey) &
                     Builders<SavedTickerDefinition>.Filter.Eq(x => x.NameKey, candidate.NameKey);
        if (excludedId.HasValue)
            filter &= Builders<SavedTickerDefinition>.Filter.Ne(x => x.Id, excludedId.Value);
        if (await _context.Definitions.Find(filter).Limit(1).FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) != null)
            throw new DuplicateSavedTickerDefinitionNameException(candidate.Name);
    }
}
