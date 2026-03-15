using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Caching.StackExchangeRedis.Tests.DependencyInjection;

/// <summary>
/// DbContext used by EF Core persistence tests.
/// </summary>
public class FunctionalTestDbContext : DbContext
{
    public FunctionalTestDbContext(DbContextOptions<FunctionalTestDbContext> options) : base(options)
    {
    }
}

/// <summary>
/// Functional tests that exercise managers and persistence providers end-to-end through DI,
/// verifying that the correct provider is resolved and works for CRUD operations.
/// </summary>
[Collection("PersistenceProviderFunctional")]
public class PersistenceProviderFunctionalTests : IDisposable
{
    private const string TestFunction = "TestFunction";

    public PersistenceProviderFunctionalTests()
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [TestFunction] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
        TickerFunctionProvider.Build();
    }

    public void Dispose()
    {
        TickerFunctionProvider.Build();
    }

    private static ServiceProvider BuildInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds an EF Core provider using an open SQLite in-memory connection.
    /// The connection must remain open for the lifetime of the test.
    /// </summary>
    private static (ServiceProvider sp, SqliteConnection connection) BuildEfCoreProvider()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<FunctionalTestDbContext>(options =>
        {
            options.UseSqlite(connection);
            options.EnableServiceProviderCaching(false);
        });

        services.AddTickerQ(options =>
        {
            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<FunctionalTestDbContext>(ConfigurationType.UseModelCustomizer);
            });
        });

        var sp = services.BuildServiceProvider();

        // Ensure the database schema is created
        using var scope = sp.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FunctionalTestDbContext>();
        dbContext.Database.EnsureCreated();

        return (sp, connection);
    }

    private static (ServiceProvider sp, SqliteConnection connection) BuildEfCoreWithRedisProvider()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<FunctionalTestDbContext>(options =>
        {
            options.UseSqlite(connection);
            options.EnableServiceProviderCaching(false);
        });

        services.AddTickerQ(options =>
        {
            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<FunctionalTestDbContext>(ConfigurationType.UseModelCustomizer);
            });

            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379";
            });
        });

        var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FunctionalTestDbContext>();
        dbContext.Database.EnsureCreated();

        return (sp, connection);
    }

    // =========================================================================
    // In-Memory Provider — Manager CRUD via ITimeTickerManager
    // =========================================================================

    [Fact]
    public async Task InMemory_TimeTickerManager_AddAndDelete_WorksEndToEnd()
    {
        await using var sp = BuildInMemoryProvider();
        var manager = sp.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();

        var entity = new TimeTickerEntity
        {
            Function = TestFunction,
            ExecutionTime = DateTime.UtcNow.AddMinutes(30)
        };

        var addResult = await manager.AddAsync(entity);

        Assert.True(addResult.IsSucceeded);
        Assert.NotNull(addResult.Result);
        Assert.NotEqual(Guid.Empty, addResult.Result.Id);

        var deleteResult = await manager.DeleteAsync(addResult.Result.Id);
        Assert.True(deleteResult.IsSucceeded);
    }

    [Fact]
    public async Task InMemory_TimeTickerManager_AddBatch_WorksEndToEnd()
    {
        await using var sp = BuildInMemoryProvider();
        var manager = sp.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();

        var entities = new List<TimeTickerEntity>
        {
            new() { Function = TestFunction, ExecutionTime = DateTime.UtcNow.AddMinutes(10) },
            new() { Function = TestFunction, ExecutionTime = DateTime.UtcNow.AddMinutes(20) },
            new() { Function = TestFunction, ExecutionTime = DateTime.UtcNow.AddMinutes(30) }
        };

        var result = await manager.AddBatchAsync(entities);

        Assert.True(result.IsSucceeded);
        Assert.Equal(3, result.Result.Count);
    }

    [Fact]
    public async Task InMemory_CronTickerManager_AddAndDelete_WorksEndToEnd()
    {
        await using var sp = BuildInMemoryProvider();
        var manager = sp.GetRequiredService<ICronTickerManager<CronTickerEntity>>();

        var entity = new CronTickerEntity
        {
            Function = TestFunction,
            Expression = "0 0 * * * *" // every hour (6-part)
        };

        var addResult = await manager.AddAsync(entity);

        Assert.True(addResult.IsSucceeded);
        Assert.NotNull(addResult.Result);
        Assert.NotEqual(Guid.Empty, addResult.Result.Id);

        var deleteResult = await manager.DeleteAsync(addResult.Result.Id);
        Assert.True(deleteResult.IsSucceeded);
    }

    // =========================================================================
    // In-Memory Provider — Persistence CRUD via ITickerPersistenceProvider
    // =========================================================================

    [Fact]
    public async Task InMemory_PersistenceProvider_AddAndGetTimeTicker()
    {
        await using var sp = BuildInMemoryProvider();
        var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = TestFunction,
            ExecutionTime = DateTime.UtcNow.AddMinutes(5),
            Status = TickerStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        var added = await persistence.AddTimeTickers([ticker]);
        Assert.Equal(1, added);

        var retrieved = await persistence.GetTimeTickerById(ticker.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(ticker.Id, retrieved.Id);
        Assert.Equal(TestFunction, retrieved.Function);
    }

    [Fact]
    public async Task InMemory_PersistenceProvider_AddUpdateAndRemoveTimeTicker()
    {
        await using var sp = BuildInMemoryProvider();
        var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = TestFunction,
            ExecutionTime = DateTime.UtcNow.AddMinutes(5),
            Status = TickerStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        await persistence.AddTimeTickers([ticker]);

        ticker.ExecutionTime = DateTime.UtcNow.AddMinutes(99);
        var updated = await persistence.UpdateTimeTickers([ticker]);
        Assert.Equal(1, updated);

        var afterUpdate = await persistence.GetTimeTickerById(ticker.Id);
        Assert.NotNull(afterUpdate);

        var removed = await persistence.RemoveTimeTickers([ticker.Id]);
        Assert.Equal(1, removed);

        var afterRemove = await persistence.GetTimeTickerById(ticker.Id);
        Assert.Null(afterRemove);
    }

    [Fact]
    public async Task InMemory_PersistenceProvider_InsertAndGetCronTicker()
    {
        await using var sp = BuildInMemoryProvider();
        var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        var cronTicker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = TestFunction,
            Expression = "*/5 * * * *",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        var inserted = await persistence.InsertCronTickers([cronTicker], CancellationToken.None);
        Assert.Equal(1, inserted);

        var retrieved = await persistence.GetCronTickerById(cronTicker.Id, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(cronTicker.Id, retrieved.Id);
        Assert.Equal("*/5 * * * *", retrieved.Expression);
    }

    [Fact]
    public async Task InMemory_PersistenceProvider_InsertUpdateRemoveCronTicker()
    {
        await using var sp = BuildInMemoryProvider();
        var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        var cronTicker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = TestFunction,
            Expression = "*/10 * * * *",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        await persistence.InsertCronTickers([cronTicker], CancellationToken.None);

        cronTicker.Expression = "0 0 * * *";
        var updated = await persistence.UpdateCronTickers([cronTicker], CancellationToken.None);
        Assert.Equal(1, updated);

        var removed = await persistence.RemoveCronTickers([cronTicker.Id], CancellationToken.None);
        Assert.Equal(1, removed);

        var afterRemove = await persistence.GetCronTickerById(cronTicker.Id, CancellationToken.None);
        Assert.Null(afterRemove);
    }

    // =========================================================================
    // EF Core Provider — Persistence CRUD via ITickerPersistenceProvider
    // =========================================================================

    [Fact]
    public async Task EfCore_PersistenceProvider_AddAndGetTimeTicker()
    {
        var (sp, connection) = BuildEfCoreProvider();
        try
        {
            var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

            Assert.Contains("EfCore", persistence.GetType().Name);

            var ticker = new TimeTickerEntity
            {
                Id = Guid.NewGuid(),
                Function = TestFunction,
                ExecutionTime = DateTime.UtcNow.AddMinutes(5),
                Status = TickerStatus.Idle,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Request = Array.Empty<byte>()
            };

            var added = await persistence.AddTimeTickers([ticker]);
            Assert.Equal(1, added);

            var retrieved = await persistence.GetTimeTickerById(ticker.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(ticker.Id, retrieved.Id);
        }
        finally
        {
            await sp.DisposeAsync();
            connection.Dispose();
        }
    }

    [Fact]
    public async Task EfCore_PersistenceProvider_InsertAndGetCronTicker()
    {
        var (sp, connection) = BuildEfCoreProvider();
        try
        {
            var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

            Assert.Contains("EfCore", persistence.GetType().Name);

            var cronTicker = new CronTickerEntity
            {
                Id = Guid.NewGuid(),
                Function = TestFunction,
                Expression = "*/5 * * * *",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Request = Array.Empty<byte>()
            };

            var inserted = await persistence.InsertCronTickers([cronTicker], CancellationToken.None);
            Assert.Equal(1, inserted);

            var retrieved = await persistence.GetCronTickerById(cronTicker.Id, CancellationToken.None);
            Assert.NotNull(retrieved);
            Assert.Equal(cronTicker.Id, retrieved.Id);
        }
        finally
        {
            await sp.DisposeAsync();
            connection.Dispose();
        }
    }

    [Fact]
    public async Task EfCore_PersistenceProvider_FullCrudTimeTicker()
    {
        var (sp, connection) = BuildEfCoreProvider();
        try
        {
            var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

            var ticker = new TimeTickerEntity
            {
                Id = Guid.NewGuid(),
                Function = TestFunction,
                ExecutionTime = DateTime.UtcNow.AddMinutes(5),
                Status = TickerStatus.Idle,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Request = Array.Empty<byte>()
            };

            // Add
            await persistence.AddTimeTickers([ticker]);

            // Update
            ticker.ExecutionTime = DateTime.UtcNow.AddMinutes(99);
            var updated = await persistence.UpdateTimeTickers([ticker]);
            Assert.Equal(1, updated);

            // Remove
            var removed = await persistence.RemoveTimeTickers([ticker.Id]);
            Assert.Equal(1, removed);

            var afterRemove = await persistence.GetTimeTickerById(ticker.Id);
            Assert.Null(afterRemove);
        }
        finally
        {
            await sp.DisposeAsync();
            connection.Dispose();
        }
    }

    // =========================================================================
    // EF Core + Redis — Verifies EF Core persists while Redis is configured
    // =========================================================================

    [Fact]
    public async Task EfCoreWithRedis_ResolvesEfCorePersistenceProvider()
    {
        var (sp, connection) = BuildEfCoreWithRedisProvider();
        try
        {
            var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

            // EF Core should take precedence over Redis
            Assert.Contains("EfCore", persistence.GetType().Name);
        }
        finally
        {
            await sp.DisposeAsync();
            connection.Dispose();
        }
    }

    [Fact]
    public async Task EfCoreWithRedis_PersistenceProvider_CrudStillWorks()
    {
        var (sp, connection) = BuildEfCoreWithRedisProvider();
        try
        {
            var persistence = sp.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

            // Verify it's EF Core
            Assert.Contains("EfCore", persistence.GetType().Name);

            var ticker = new TimeTickerEntity
            {
                Id = Guid.NewGuid(),
                Function = TestFunction,
                ExecutionTime = DateTime.UtcNow.AddMinutes(5),
                Status = TickerStatus.Idle,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Request = Array.Empty<byte>()
            };

            // CRUD should work through EF Core even when Redis is configured
            var added = await persistence.AddTimeTickers([ticker]);
            Assert.Equal(1, added);

            var retrieved = await persistence.GetTimeTickerById(ticker.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(ticker.Id, retrieved.Id);

            var removed = await persistence.RemoveTimeTickers([ticker.Id]);
            Assert.Equal(1, removed);
        }
        finally
        {
            await sp.DisposeAsync();
            connection.Dispose();
        }
    }
}
