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
    private FilterDefinition<SchedulePolicy> PolicyInPartition(Guid id)
        => Builders<SchedulePolicy>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey) &
           Builders<SchedulePolicy>.Filter.Eq(x => x.Id, id);

    public async Task<IReadOnlyList<SchedulePolicy>> ListSchedulePoliciesAsync(
        CancellationToken cancellationToken = default)
        => await _context.SchedulePolicies.Find(
                Builders<SchedulePolicy>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey))
            .SortBy(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<SchedulePolicy> GetSchedulePolicyAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.SchedulePolicies.Find(PolicyInPartition(id)).FirstOrDefaultAsync(cancellationToken);

    public Task<SchedulePolicy> GetSchedulePolicyByNameAsync(string name,
        CancellationToken cancellationToken = default)
        => _context.SchedulePolicies.Find(
            Builders<SchedulePolicy>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey) &
            Builders<SchedulePolicy>.Filter.Eq(x => x.NameKey, SchedulePolicyNames.CreateKey(name)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SchedulePolicy> CreateSchedulePolicyAsync(SchedulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var row = policy.Copy();
        row.Id = row.Id == Guid.Empty ? Guid.NewGuid() : row.Id;
        row.ApplicationNamespaceKey = _runtimePartitionKey;
        row.NameKey = SchedulePolicyNames.CreateKey(row.Name);
        row.CreatedAtUtc = row.CreatedAtUtc == default ? _clock.UtcNow : row.CreatedAtUtc;
        row.UpdatedAtUtc = _clock.UtcNow;
        row.Version = 1;
        try { await _context.SchedulePolicies.InsertOneAsync(row, cancellationToken: cancellationToken).ConfigureAwait(false); }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        { throw new DuplicateSchedulePolicyNameException(row.Name); }
        return row;
    }

    public async Task<SchedulePolicy> UpdateSchedulePolicyAsync(SchedulePolicy policy, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var current = await _context.SchedulePolicies.Find(
                PolicyInPartition(policy.Id) & Builders<SchedulePolicy>.Filter.Eq(x => x.Version, expectedVersion))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (current == null) return null;
        var next = policy.Copy();
        next.ApplicationNamespaceKey = _runtimePartitionKey;
        next.NameKey = SchedulePolicyNames.CreateKey(next.Name);
        next.CreatedAtUtc = current.CreatedAtUtc;
        next.UpdatedAtUtc = _clock.UtcNow;
        next.Version = expectedVersion + 1;
        try
        {
            return await _context.SchedulePolicies.FindOneAndReplaceAsync(
                PolicyInPartition(policy.Id) & Builders<SchedulePolicy>.Filter.Eq(x => x.Version, expectedVersion),
                next, new FindOneAndReplaceOptions<SchedulePolicy> { ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        { throw new DuplicateSchedulePolicyNameException(policy.Name); }
    }

    public async Task<bool> DeleteSchedulePolicyAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
        => (await _context.SchedulePolicies.DeleteOneAsync(
            PolicyInPartition(id) & Builders<SchedulePolicy>.Filter.Eq(x => x.Version, expectedVersion),
            cancellationToken).ConfigureAwait(false)).DeletedCount == 1;

    public async Task<bool> IsSchedulePolicyInUseAsync(string name,
        CancellationToken cancellationToken = default)
        => await _context.TimeTickers.Find(
               Builders<TTimeTicker>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey) &
               Builders<TTimeTicker>.Filter.Eq(x => x.SchedulePolicyName, name)).Limit(1)
               .AnyAsync(cancellationToken).ConfigureAwait(false) ||
           await _context.CronTickers.Find(
               Builders<TCronTicker>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey) &
               Builders<TCronTicker>.Filter.Eq(x => x.SchedulePolicyName, name)).Limit(1)
               .AnyAsync(cancellationToken).ConfigureAwait(false);
}
