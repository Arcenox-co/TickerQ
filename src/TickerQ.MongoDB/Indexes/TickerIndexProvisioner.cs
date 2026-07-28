using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities.Entities;

namespace TickerQ.MongoDB.Indexes
{
    internal sealed class TickerIndexProvisioner<TTimeTicker, TCronTicker> : IHostedService
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerMongoContext<TTimeTicker, TCronTicker> _context;

        public TickerIndexProvisioner(ITickerMongoContext<TTimeTicker, TCronTicker> context)
        {
            _context = context;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var fence = _context.Database.GetCollection<BsonDocument>(
                _context.TimeTickers.CollectionNamespace.CollectionName + "_GraphFence");
            await fence.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", "time-ticker-graph"),
                Builders<BsonDocument>.Update.SetOnInsert("Version", 0L),
                new UpdateOptions { IsUpsert = true },
                cancellationToken).ConfigureAwait(false);
            await CreateTimeTickerIndexes(cancellationToken).ConfigureAwait(false);
            await CreateCronTickerIndexes(cancellationToken).ConfigureAwait(false);
            await CreateCronOccurrenceIndexes(cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Task CreateTimeTickerIndexes(CancellationToken ct)
        {
            var keys = Builders<TTimeTicker>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<TTimeTicker>(keys.Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_TimeTicker_ExecutionTime" }),
                new CreateIndexModel<TTimeTicker>(keys.Ascending(x => x.Status).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_TimeTicker_Status_ExecutionTime" }),
                new CreateIndexModel<TTimeTicker>(keys.Ascending(x => x.ParentId),
                    new CreateIndexOptions { Name = "IX_TimeTicker_ParentId", Sparse = true }),
                new CreateIndexModel<TTimeTicker>(
                    keys.Ascending(x => x.ParentId).Ascending(x => x.Status)
                        .Ascending(x => x.ExecutedAt).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "IX_TimeTicker_Retention" }),
                new CreateIndexModel<TTimeTicker>(
                    keys.Ascending(x => x.LockHolder).Ascending(x => x.LeaseUntil),
                    new CreateIndexOptions { Name = "IX_TimeTicker_RetentionClaim" }),
            };
            return _context.TimeTickers.Indexes.CreateManyAsync(models, ct);
        }

        private Task CreateCronTickerIndexes(CancellationToken ct)
        {
            var keys = Builders<TCronTicker>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<TCronTicker>(keys.Ascending(x => x.Expression),
                    new CreateIndexOptions { Name = "IX_CronTickers_Expression" }),
                new CreateIndexModel<TCronTicker>(keys.Ascending(x => x.Function).Ascending(x => x.Expression),
                    new CreateIndexOptions { Name = "IX_Function_Expression" }),
            };
            return _context.CronTickers.Indexes.CreateManyAsync(models, ct);
        }

        private Task CreateCronOccurrenceIndexes(CancellationToken ct)
        {
            var keys = Builders<CronTickerOccurrenceEntity<TCronTicker>>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.CronTickerId),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_CronTickerId" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_ExecutionTime" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.Status).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_Status_ExecutionTime" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.Status).Ascending(x => x.ExecutedAt).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_Retention" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.CronTickerId).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "UQ_CronTickerId_ExecutionTime", Unique = true }),
            };
            return _context.CronTickerOccurrences.Indexes.CreateManyAsync(models, ct);
        }
    }
}
