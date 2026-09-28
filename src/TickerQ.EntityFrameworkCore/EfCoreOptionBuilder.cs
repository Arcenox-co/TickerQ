using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    public class TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        internal Action<IServiceCollection> ConfigureServices { get; set; }
        internal int PoolSize { get; set; } = 1024;
        internal string Schema { get; set; } = "ticker";
        internal AssistantHistoryOptions AssistantHistory { get; set; }
        internal bool AutoMigrate { get; set; }
        internal TickerQRuntimePartition LegacyRuntimeOwner { get; private set; }
        internal long? LegacyRuntimeAdoptionEpoch { get; private set; }
        internal bool LegacyWritersDrained { get; private set; }

        /// <summary>
        /// Explicitly identifies the sole namespace permitted to claim pre-partition runtime rows.
        /// This is only an adoption prerequisite; legacy rows are never inferred or reassigned merely
        /// because a scheduler is configured with an application namespace.
        /// </summary>
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseLegacyRuntimePartitionAdoption(
            string ownerApplicationNamespace, long epoch, bool legacyWritersDrained)
        {
            if (epoch <= 0)
                throw new ArgumentOutOfRangeException(nameof(epoch), epoch,
                    "The legacy runtime adoption epoch must be positive.");
            if (!legacyWritersDrained)
                throw new ArgumentException(
                    "Legacy runtime adoption requires positive confirmation that all pre-partition writers are drained.",
                    nameof(legacyWritersDrained));

            var owner = new TickerQRuntimePartition(ownerApplicationNamespace);
            if (LegacyRuntimeOwner != null &&
                (!LegacyRuntimeOwner.Equals(owner) || LegacyRuntimeAdoptionEpoch != epoch))
                throw new InvalidOperationException(
                    "Legacy runtime partition ownership is already configured and cannot be redirected.");

            LegacyRuntimeOwner = owner;
            LegacyRuntimeAdoptionEpoch = epoch;
            LegacyWritersDrained = true;
            return this;
        }

        /// <summary>
        /// Apply pending EF Core migrations for the TickerQ DbContext automatically
        /// at host startup, before seeding/scheduling touch the store. Opt-in: skip
        /// this if your pipeline runs <c>dotnet ef database update</c> — apps
        /// shouldn't mutate schema on boot unless you decided they should.
        /// </summary>
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> AutoMigrateDatabase()
        {
            AutoMigrate = true;
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseApplicationDbContext<TDbContext>(ConfigurationType configurationType) where TDbContext : DbContext
        {
            ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeTicker, TCronTicker>(this, configurationType);
            return this;
        }
        
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseTickerQDbContext<TDbContext>(Action<DbContextOptionsBuilder> optionsAction, string schema = null) where TDbContext : TickerQDbContext<TTimeTicker, TCronTicker>
        {
            Schema = schema ?? Schema;
            
            ServiceBuilder.UseTickerQDbContext<TDbContext, TTimeTicker, TCronTicker>(this, optionsAction);
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetDbContextPoolSize(int poolSize)
        {
            PoolSize = poolSize;
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetSchema(string schema)
        {
            Schema = schema;
            return this;
        }

        /// <summary>
        /// Opt in to EF-backed per-user chat history for the dashboard AI
        /// assistant. Adds two tables (AssistantConversations,
        /// AssistantMessages) to the TickerQ model — they ride your normal
        /// migrations flow — and registers the
        /// <see cref="TickerQ.Utilities.Interfaces.IAssistantHistoryStore"/>
        /// the dashboard persists through. Without this call, no tables are
        /// mapped and no history is stored.
        /// </summary>
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> AddAssistantHistory(
            Action<AssistantHistoryOptions> configure = null)
        {
            var options = new AssistantHistoryOptions();
            configure?.Invoke(options);
            AssistantHistory = options;
            // Design-time fallback (dotnet ef migrations) — see AssistantHistoryOptions.Current.
            AssistantHistoryOptions.Current = options;
            return this;
        }
    }
}
