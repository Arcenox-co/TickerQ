using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal abstract class BasePersistenceProvider<TDbContext> where TDbContext : DbContext
    {
        protected readonly TDbContext DbContext;

        protected BasePersistenceProvider(TDbContext dbContext)
        {
            DbContext = dbContext;
        }

        protected DbSet<TEntity> GetDbSet<TEntity>() where TEntity : class
            => DbContext.Set<TEntity>();

        protected void Upsert<TEntity>(
            TEntity entity,
            Func<EntityEntry<TEntity>, bool> match,
            EntityState state = EntityState.Modified)
            where TEntity : class
        {
            var tracked = DbContext.ChangeTracker.Entries<TEntity>()
                .FirstOrDefault(match);

            if (tracked != null)
            {
                tracked.CurrentValues.SetValues(entity);
            }
            else
            {
                DbContext.Attach(entity).State = state;
            }
        }

        protected void UpsertRange<TEntity>(
            IEnumerable<TEntity> entities,
            Func<TEntity, object> keySelector,
            EntityState state = EntityState.Modified)
            where TEntity : class
        {
            foreach (var entity in entities)
            {
                Upsert(entity, e => Equals(keySelector(e.Entity), keySelector(entity)), state);
            }
        }

        protected void Delete<TEntity>(
            TEntity entity,
            Func<TEntity, object> keySelector)
            where TEntity : class
        {
            var tracked = DbContext.ChangeTracker.Entries<TEntity>()
                .FirstOrDefault(e => Equals(keySelector(e.Entity), keySelector(entity)));

            if (tracked != null)
                DbContext.Remove(tracked.Entity);
            else
                DbContext.Remove(entity); // Will attach automatically if needed
        }

        protected void DeleteRange<TEntity>(
            IEnumerable<TEntity> entities,
            Func<TEntity, object> keySelector)
            where TEntity : class
        {
            foreach (var entity in entities)
            {
                Delete(entity, keySelector);
            }
        }

        protected async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }

        protected void DetachAll<TEntity>() where TEntity : class
        {
            foreach (var entry in DbContext.ChangeTracker.Entries<TEntity>())
                entry.State = EntityState.Detached;
        }

        protected async Task SaveAndDetachAsync<TEntity>(CancellationToken cancellationToken = default)
            where TEntity : class
        {
            await SaveChangesAsync(cancellationToken);
            DetachAll<TEntity>();
        }
        
        protected static bool IsUniqueConstraintViolation(DbUpdateException ex, string expectedConstraintName = null)
        {
            if (ex.InnerException == null)
                return false;

            var message = ex.InnerException.Message.ToLowerInvariant();

            // Keywords common across providers
            var isUniqueViolation =
                message.Contains("unique constraint") ||
                message.Contains("duplicate key") ||
                message.Contains("unique index") ||
                message.Contains("constraint failed");

            if (!isUniqueViolation)
                return false;

            return string.IsNullOrEmpty(expectedConstraintName) ||
                   message.Contains(expectedConstraintName.ToLowerInvariant());
        }
    }
}