using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Infrastructure
{
    internal sealed class MongoTickerQueryable<TEntity> : ITickerQueryable<TEntity>
        where TEntity : class
    {
        private readonly IMongoCollection<TEntity> _collection;
        private readonly Func<TEntity, TickerRelation[], CancellationToken, Task> _relationLoader;
        private readonly IQueryable<TEntity> _query;
        private readonly TickerRelation[] _relations;

        public MongoTickerQueryable(
            IMongoCollection<TEntity> collection,
            Func<TEntity, TickerRelation[], CancellationToken, Task> relationLoader)
            : this(collection, relationLoader, collection.AsQueryable(), [])
        {
        }

        private MongoTickerQueryable(
            IMongoCollection<TEntity> collection,
            Func<TEntity, TickerRelation[], CancellationToken, Task> relationLoader,
            IQueryable<TEntity> query,
            TickerRelation[] relations)
        {
            _collection = collection;
            _relationLoader = relationLoader;
            _query = query;
            _relations = relations;
        }

        private MongoTickerQueryable<TEntity> With(IQueryable<TEntity> next)
            => new(_collection, _relationLoader, next, _relations);

        public ITickerQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate) => With(_query.Where(predicate));
        public ITickerQueryable<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) => With(_query.OrderBy(keySelector));
        public ITickerQueryable<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) => With(_query.OrderByDescending(keySelector));
        public ITickerQueryable<TEntity> Skip(int count) => With(_query.Skip(count));
        public ITickerQueryable<TEntity> Take(int count) => With(_query.Take(count));
        public ITickerQueryable<TEntity> AsNoTracking() => this; // Mongo driver doesn't track entities

        public ITickerQueryable<TEntity> WithRelated(params TickerRelation[] relations)
        {
            if (relations.Length == 0) return this;
            var merged = new TickerRelation[_relations.Length + relations.Length];
            _relations.CopyTo(merged, 0);
            relations.CopyTo(merged, _relations.Length);
            return new MongoTickerQueryable<TEntity>(_collection, _relationLoader, _query, merged);
        }

        public async Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default)
        {
            var list = await _query.ToListAsync(cancellationToken).ConfigureAwait(false);
            await ApplyRelations(list, cancellationToken).ConfigureAwait(false);
            return list.ToArray();
        }

        public async Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            var item = await _query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (item != null) await ApplyRelations([item], cancellationToken).ConfigureAwait(false);
            return item;
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
            => await _query.CountAsync(cancellationToken).ConfigureAwait(false);

        public async Task<PaginationResult<TEntity>> ToPaginatedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 1000);

            var count = await _query.CountAsync(cancellationToken).ConfigureAwait(false);
            var items = await _query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            await ApplyRelations(items, cancellationToken).ConfigureAwait(false);
            return new PaginationResult<TEntity>(items, count, pageNumber, pageSize);
        }

        private async Task ApplyRelations(System.Collections.Generic.IEnumerable<TEntity> items, CancellationToken ct)
        {
            if (_relations.Length == 0 || _relationLoader == null) return;
            foreach (var item in items)
                await _relationLoader(item, _relations, ct).ConfigureAwait(false);
        }
    }
}
