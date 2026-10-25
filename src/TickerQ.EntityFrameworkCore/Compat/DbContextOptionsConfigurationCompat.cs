using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if NET9_0_OR_GREATER
using Microsoft.EntityFrameworkCore.Infrastructure;
#else
using TickerQ.EntityFrameworkCore.Customizer;
#endif

#if !NET9_0_OR_GREATER
namespace TickerQ.EntityFrameworkCore.Customizer
{
    /// <summary>
    /// Configures the options of an application DbContext. EF Core 9 adds this interface to
    /// Microsoft.EntityFrameworkCore.Infrastructure and applies every registration itself; on EF Core 8,
    /// TickerQ applies them by wrapping the context's DbContextOptions registration.
    /// </summary>
    public interface IDbContextOptionsConfiguration<TContext> where TContext : DbContext
    {
        void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder);
    }
}
#endif

namespace TickerQ.EntityFrameworkCore.Compat
{
    internal static class DbContextOptionsConfigurationCompat
    {
        /// <summary>Registers <typeparamref name="TConfiguration"/> for the application's <typeparamref name="TContext"/>.</summary>
        public static void Add<TContext, TConfiguration>(IServiceCollection services)
            where TContext : DbContext
            where TConfiguration : class, IDbContextOptionsConfiguration<TContext>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbContextOptionsConfiguration<TContext>, TConfiguration>());
#if !NET9_0_OR_GREATER
            ApplyThroughOptionsRegistration<TContext>(services);
#endif
        }

#if !NET9_0_OR_GREATER
        // AddDbContext, AddDbContextPool, and AddDbContextFactory all resolve DbContextOptions<TContext> from
        // the container, so wrapping that registration reaches every way the context is created.
        private static void ApplyThroughOptionsRegistration<TContext>(IServiceCollection services)
            where TContext : DbContext
        {
            var original = services.LastOrDefault(x => x.ServiceType == typeof(DbContextOptions<TContext>));
            if (original == null)
                throw new InvalidOperationException(
                    $"On EF Core 8, register {typeof(TContext).Name} with AddDbContext, AddDbContextPool, or " +
                    "AddDbContextFactory before calling AddTickerQ, so TickerQ can configure its options.");
            if (original.ImplementationFactory?.Target is ConfiguredOptions<TContext>)
                return;

            services.Remove(original);
            services.Add(new ServiceDescriptor(typeof(DbContextOptions<TContext>),
                new ConfiguredOptions<TContext>(original).Create, original.Lifetime));
        }

        private sealed class ConfiguredOptions<TContext>(ServiceDescriptor original) where TContext : DbContext
        {
            public object Create(IServiceProvider serviceProvider)
            {
                var options = (DbContextOptions<TContext>)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(serviceProvider)
                    ?? ActivatorUtilities.CreateInstance(serviceProvider, original.ImplementationType!));
                var builder = new DbContextOptionsBuilder<TContext>(options);
                foreach (var configuration in serviceProvider.GetServices<IDbContextOptionsConfiguration<TContext>>())
                    configuration.Configure(serviceProvider, builder);
                return builder.Options;
            }
        }
#endif
    }
}
