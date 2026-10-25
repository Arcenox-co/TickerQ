using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfStoreMetadataKeyTests
{
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void ActivationKey_FitsTheProviderModelAndGeneratedSchema(string provider)
    {
        var builder = new DbContextOptionsBuilder<TestTickerQDbContext>();
        switch (provider)
        {
            case "PostgreSql": builder.UseNpgsql("Host=localhost;Database=metadata_model"); break;
            case "SqlServer": builder.UseSqlServer("Server=localhost;Database=metadata_model"); break;
            default: builder.UseSqlite("Data Source=:memory:"); break;
        }

        using var context = new TestTickerQDbContext(builder.Options);
        var property = context.Model.FindEntityType(typeof(TickerQStoreMetadata))!
            .FindProperty(nameof(TickerQStoreMetadata.Id))!;
        var key = new ReconciliationActivationScope("orders-api").ScopeKey;
        Assert.Equal(78, key.Length);
        Assert.Equal(80, property.GetMaxLength());
        Assert.True(key.Length <= property.GetMaxLength());

        // SQLite does not enforce max lengths. Check each real provider's DDL as well
        // so the fast suite catches a regression even when integration tests are disabled.
        var script = context.Database.GenerateCreateScript();
        if (provider == "PostgreSql")
            Assert.Contains("\"Id\" character varying(80) NOT NULL", script);
        else if (provider == "SqlServer")
            Assert.Contains("[Id] nvarchar(80) NOT NULL", script);
    }
}

internal static class StoreMetadataActivationContract
{
    internal static async Task VerifyAsync(DbContextOptions<TestTickerQDbContext> options)
    {
        var scope = new ReconciliationActivationScope("metadata-length-regression");
        await using (var schema = new TestTickerQDbContext(options))
        {
            await schema.Database.EnsureCreatedAsync();
            // An existing singleton row must keep its values when the activation row is added.
            schema.Add(new TickerQStoreMetadata
            {
                ApplicationNamespaceKey = scope.ScopeKey,
                Id = TickerQStoreMetadata.SingletonId,
                SchemaVersion = 1,
                DataVersion = 2,
                Version = 3,
                LastMigrationId = "existing-migration",
                UpdatedAtUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
            });
            await schema.SaveChangesAsync();

        }

        await using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options))
            .BuildServiceProvider();
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope(scope.ApplicationNamespace, 1, true);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        var provider = new TestableProvider(services, clock, scheduler, Substitute.For<ITickerQRedisContext>());
        var begun = await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        Assert.Equal(ActivationEpochPhase.Activating, begun.Phase);
        await provider.AdvanceReconciliationCheckpointAsync(scope, 1, "04-finalization-complete");
        Assert.True((await provider.CommitReconciliationActivationEpochAsync(scope, 1)).IsActivatedFor(1));

        var restarted = new TestableProvider(services, clock, scheduler, Substitute.For<ITickerQRedisContext>());
        Assert.True((await restarted.GetReconciliationActivationStateAsync(scope)).IsActivatedFor(1));
        await using var verify = new TestTickerQDbContext(options);
        var activation = await verify.Set<TickerQStoreMetadata>().AsNoTracking()
            .SingleAsync(x => x.ApplicationNamespaceKey == scope.ScopeKey && x.Id == scope.ScopeKey);
        Assert.Equal(78, activation.Id.Length);
        Assert.Equal("04-finalization-complete", activation.ActivationCheckpoint);
        var existing = await verify.Set<TickerQStoreMetadata>().AsNoTracking()
            .SingleAsync(x => x.ApplicationNamespaceKey == scope.ScopeKey && x.Id == TickerQStoreMetadata.SingletonId);
        Assert.Equal(1, existing.SchemaVersion);
        Assert.Equal(2, existing.DataVersion);
        Assert.Equal(3, existing.Version);
        Assert.Equal("existing-migration", existing.LastMigrationId);
    }
}
