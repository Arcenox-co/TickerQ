using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.Tests.DependencyInjection;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }
}

public class PersistenceProviderRegistrationTests
{
    [Fact]
    public void AddTickerQ_WithoutExternalProviders_RegistersInMemoryProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTickerQ();

        // Assert
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("InMemory", persistenceProvider.GetType().Name);
    }

    [Fact]
    public void AddTickerQ_WithRedis_RegistersRedisPersistenceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379";
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("Redis", persistenceProvider.GetType().Name);
    }

    [Fact]
    public void AddTickerQ_WithEfCore_RegistersEfCorePersistenceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite("Data Source=PersistenceTest;Mode=Memory;Cache=Shared");
        });

        // Act
        services.AddTickerQ(options =>
        {
            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<TestDbContext>(ConfigurationType.UseModelCustomizer);
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("EfCore", persistenceProvider.GetType().Name);
    }

    [Fact]
    public void AddTickerQ_WithEfCoreAndRedis_EfCoreTakesPrecedence()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite("Data Source=PrecedenceTest;Mode=Memory;Cache=Shared");
        });

        // Act — EF Core registered first, then Redis
        services.AddTickerQ(options =>
        {
            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<TestDbContext>(ConfigurationType.UseModelCustomizer);
            });

            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379";
            });
        });

        // Assert — EF Core should win because it uses AddSingleton, Redis uses TryAddSingleton
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("EfCore", persistenceProvider.GetType().Name);
    }

    [Fact]
    public void AddTickerQ_WithRedisAndEfCore_EfCoreTakesPrecedence()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite("Data Source=ReversePrecedenceTest;Mode=Memory;Cache=Shared");
        });

        // Act — Redis registered first, then EF Core (reversed order)
        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379";
            });

            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<TestDbContext>(ConfigurationType.UseModelCustomizer);
            });
        });

        // Assert — EF Core should still win regardless of registration order
        // because EF Core uses AddSingleton (replaces any prior) and Redis uses TryAddSingleton (skips if exists)
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("EfCore", persistenceProvider.GetType().Name);
    }
}
