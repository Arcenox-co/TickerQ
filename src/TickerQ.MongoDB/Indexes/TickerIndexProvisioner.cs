using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
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
                    keys.Ascending(x => x.CronTickerId).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "UQ_CronTickerId_ExecutionTime", Unique = true }),
            };
            return _context.CronTickerOccurrences.Indexes.CreateManyAsync(models, ct);
        }
    }
}
