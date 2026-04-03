using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal class EfTickerQueryable<TDbContext, TEntity> : ITickerQueryable<TEntity>
        where TDbContext : DbContext
        where TEntity : class
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<IQueryable<TEntity>, TickerRelation[], IQueryable<TEntity>> _relationApplier;
        private readonly Func<IQueryable<TEntity>, IQueryable<TEntity>> _pipeline;

        internal EfTickerQueryable(
            IServiceProvider serviceProvider,
            Func<IQueryable<TEntity>, TickerRelation[], IQueryable<TEntity>> relationApplier = null,
            Func<IQueryable<TEntity>, IQueryable<TEntity>> pipeline = null)
        {
            _serviceProvider = serviceProvider;
            _relationApplier = relationApplier;
            _pipeline = pipeline ?? (q => q);
        }

        private EfTickerQueryable<TDbContext, TEntity> Append(Func<IQueryable<TEntity>, IQueryable<TEntity>> step)
        {
            var currentPipeline = _pipeline;
            return new EfTickerQueryable<TDbContext, TEntity>(
                _serviceProvider,
                _relationApplier,
                q => step(currentPipeline(q)));
        }

        public ITickerQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
            => Append(q => q.Where(predicate));

        public ITickerQueryable<TEntity> WithRelated(params TickerRelation[] relations)
        {
            if (_relationApplier == null)
                return this;

            var applier = _relationApplier;
            var rels = relations;
            return Append(q => applier(q, rels));
        }

        public ITickerQueryable<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
            => Append(q => q.OrderBy(keySelector));

        public ITickerQueryable<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
            => Append(q => q.OrderByDescending(keySelector));

        public ITickerQueryable<TEntity> Skip(int count)
            => Append(q => q.Skip(count));

        public ITickerQueryable<TEntity> Take(int count)
            => Append(q => q.Take(count));

        public ITickerQueryable<TEntity> AsNoTracking()
            => Append(q => q.AsNoTracking());

        public async Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default)
        {
            using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
            return await BuildQuery(session.Context).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
            return await BuildQuery(session.Context).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
            return await BuildQuery(session.Context).CountAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<PaginationResult<TEntity>> ToPaginatedAsync(
            int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 1000);

            using var session = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
            var query = BuildQuery(session.Context);

            var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return new PaginationResult<TEntity>(items, count, pageNumber, pageSize);
        }

        private IQueryable<TEntity> BuildQuery(DbContext context)
            => _pipeline(context.Set<TEntity>());
    }
}
