using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure
{
    /// <summary>
    /// In-memory ITickerQueryable implementation.
    /// Loads all entities via the provided loader, then applies filters/sort/pagination in-memory.
    /// Suitable for Redis, in-memory, or any provider that doesn't have a native query pipeline.
    /// </summary>
    public sealed class InMemoryTickerQueryable<TEntity> : ITickerProjectionQueryable<TEntity>
    {
        private readonly Func<CancellationToken, Task<List<TEntity>>> _loader;
        private readonly Func<CancellationToken, Task<List<TEntity>>> _projectionLoader;
        public bool SupportsProjection => _projectionLoader != null;
        private readonly Func<IEnumerable<TEntity>, IEnumerable<TEntity>> _pipeline;

        /// <param name="loader">
        /// Async function that loads all candidate entities.
        /// Called once when a terminal method (ToArrayAsync, FirstOrDefaultAsync, etc.) is invoked.
        /// </param>
        public InMemoryTickerQueryable(Func<CancellationToken, Task<List<TEntity>>> loader)
            : this(loader, q => q, null)
        {
        }

        public InMemoryTickerQueryable(Func<CancellationToken, Task<List<TEntity>>> loader,
            Func<CancellationToken, Task<List<TEntity>>> projectionLoader)
            : this(loader, q => q, projectionLoader)
        {
        }

        private InMemoryTickerQueryable(
            Func<CancellationToken, Task<List<TEntity>>> loader,
            Func<IEnumerable<TEntity>, IEnumerable<TEntity>> pipeline,
            Func<CancellationToken, Task<List<TEntity>>> projectionLoader)
        {
            _loader = loader;
            _pipeline = pipeline;
            _projectionLoader = projectionLoader;
        }

        private InMemoryTickerQueryable<TEntity> Append(Func<IEnumerable<TEntity>, IEnumerable<TEntity>> step)
        {
            var currentPipeline = _pipeline;
            return new InMemoryTickerQueryable<TEntity>(_loader, q => step(currentPipeline(q)), _projectionLoader);
        }

        public ITickerQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            var filter = predicate.CompileAotSafe();
            return Append(q => q.Where(filter));
        }

        public ITickerQueryable<TEntity> WithRelated(params TickerRelation[] relations)
            => this; // In-memory: relations are already loaded by the loader

        public ITickerQueryable<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            ArgumentNullException.ThrowIfNull(keySelector);
            var selector = keySelector.CompileAotSafe();
            return Append(q => q.OrderBy(selector));
        }

        public ITickerQueryable<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            ArgumentNullException.ThrowIfNull(keySelector);
            var selector = keySelector.CompileAotSafe();
            return Append(q => q.OrderByDescending(selector));
        }

        public ITickerQueryable<TEntity> Skip(int count)
            => Append(q => q.Skip(count));

        public ITickerQueryable<TEntity> Take(int count)
            => Append(q => q.Take(count));

        public ITickerQueryable<TEntity> AsNoTracking()
            => this; // No-op for in-memory

        public async Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default)
        {
            var items = await _loader(cancellationToken).ConfigureAwait(false);
            return _pipeline(items).ToArray();
        }

        public async Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            var items = await _loader(cancellationToken).ConfigureAwait(false);
            return _pipeline(items).FirstOrDefault();
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            var items = await _loader(cancellationToken).ConfigureAwait(false);
            return _pipeline(items).Count();
        }

        public async Task<TResult[]> ToArrayAsync<TResult>(Expression<Func<TEntity, TResult>> selector,
            CancellationToken cancellationToken = default)
        {
            var items = await (_projectionLoader ?? _loader)(cancellationToken).ConfigureAwait(false);
            return _pipeline(items).Select(selector.CompileAotSafe()).ToArray();
        }

        public async Task<PaginationResult<TResult>> ToPaginatedAsync<TResult>(Expression<Func<TEntity, TResult>> selector,
            int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 1000);
            var items = await (_projectionLoader ?? _loader)(cancellationToken).ConfigureAwait(false);
            var query = _pipeline(items);
            var count = query.Count();
            var project = selector.CompileAotSafe();
            var page = query.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(project).ToArray();
            return new PaginationResult<TResult>(page, count, pageNumber, pageSize);
        }

        public async Task<PaginationResult<TEntity>> ToPaginatedAsync(
            int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 1000);

            var items = await _loader(cancellationToken).ConfigureAwait(false);
            var query = _pipeline(items);

            var count = query.Count();
            var page = query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArray();

            return new PaginationResult<TEntity>(page, count, pageNumber, pageSize);
        }
    }
}
