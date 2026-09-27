using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfTimeTickerStoreUpgradeTests
{
    [Fact]
    public void Operational_model_maps_store_metadata_checkpoint()
    {
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new TestTickerQDbContext(options);
        var metadata = context.Model.FindEntityType(typeof(TickerQStoreMetadata));

        Assert.NotNull(metadata);
        Assert.Equal("TickerQStoreMetadata", metadata!.GetTableName());
        Assert.NotNull(metadata.FindProperty(nameof(TickerQStoreMetadata.SchemaVersion)));
        Assert.NotNull(metadata.FindProperty(nameof(TickerQStoreMetadata.DataVersion)));
        Assert.NotNull(metadata.FindProperty(nameof(TickerQStoreMetadata.LastMigrationId)));
        Assert.True(metadata.FindProperty(nameof(TickerQStoreMetadata.Version))!.IsConcurrencyToken);
        Assert.NotNull(metadata.FindProperty(nameof(TickerQStoreMetadata.ActivationEpoch)));
        Assert.NotNull(metadata.FindProperty(nameof(TickerQStoreMetadata.ActivationPhase)));
        Assert.NotNull(metadata.FindProperty(nameof(TickerQStoreMetadata.ActivationCheckpoint)));
    }

    [Fact]
    public async Task Pre_chain_fixture_backfills_every_descendant_and_preserves_identity_status_and_result()
    {
        await using var fixture = await UpgradeFixture.CreateAsync();
        var root = fixture.Ticker(TickerStatus.Done);
        var child = fixture.Ticker(TickerStatus.Failed, root.Id);
        var grandchild = fixture.Ticker(TickerStatus.Skipped, child.Id);
        fixture.Context.AddRange(root, child, grandchild);
        fixture.Context.Add(new TimeTickerResultEntity<TimeTickerEntity>
        {
            TickerId = child.Id, Payload = [1, 2, 3], EnvelopeVersion = 1, MediaType = "application/json"
        });
        await fixture.Context.SaveChangesAsync();

        await fixture.Pipeline.RunAsync(fixture.Context);
        fixture.Context.ChangeTracker.Clear();

        var rows = await fixture.Context.Set<TimeTickerEntity>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        Assert.All(rows, x => Assert.Equal(root.Id, x.ChainRootId));
        Assert.Equal(TickerStatus.Done, rows.Single(x => x.Id == root.Id).Status);
        Assert.Equal(TickerStatus.Failed, rows.Single(x => x.Id == child.Id).Status);
        Assert.Equal(TickerStatus.Skipped, rows.Single(x => x.Id == grandchild.Id).Status);
        Assert.Equal([1, 2, 3], (await fixture.Context.Set<TimeTickerResultEntity<TimeTickerEntity>>()
            .AsNoTracking().SingleAsync()).Payload);

        var checkpoint = await fixture.Context.Set<TickerQStoreMetadata>().AsNoTracking()
            .SingleAsync(x => x.Id == TickerQStoreMetadata.SingletonId);
        Assert.Equal(EfCoreDataMigrationPipeline.CurrentSchemaVersion, checkpoint.SchemaVersion);
        Assert.Equal(TimeTickerChainRootDataMigration<TimeTickerEntity>.Version, checkpoint.DataVersion);
        Assert.Equal(TimeTickerChainRootDataMigration<TimeTickerEntity>.Id, checkpoint.LastMigrationId);

        var checkpointTime = checkpoint.UpdatedAtUtc;
        await fixture.Pipeline.RunAsync(fixture.Context);
        Assert.Equal(checkpointTime, (await fixture.Context.Set<TickerQStoreMetadata>().AsNoTracking()
            .SingleAsync(x => x.Id == TickerQStoreMetadata.SingletonId)).UpdatedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_orphan_or_cycle_fails_with_typed_actionable_error(bool cycle)
    {
        await using var fixture = await UpgradeFixture.CreateAsync();
        var first = fixture.Ticker(TickerStatus.Idle);
        var second = fixture.Ticker(TickerStatus.Idle, first.Id);
        fixture.Context.AddRange(first, second);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        if (cycle)
            await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE TimeTickers SET ParentId = {second.Id} WHERE Id = {first.Id}");
        else
            await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE TimeTickers SET ParentId = {Guid.NewGuid()} WHERE Id = {first.Id}");
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");

        var error = await Assert.ThrowsAsync<TickerQDataMigrationException>(() =>
            fixture.Pipeline.RunAsync(fixture.Context));

        Assert.Equal(cycle ? TickerQMalformedGraphKind.Cycle : TickerQMalformedGraphKind.Orphan,
            error.GraphKind);
        Assert.Contains(first.Id.ToString(), error.Message);
        Assert.Contains("ParentId", error.Message);
        Assert.Empty(await fixture.Context.Set<TickerQStoreMetadata>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Newer_store_version_is_rejected_without_mutation()
    {
        await using var fixture = await UpgradeFixture.CreateAsync();
        fixture.Context.Add(new TickerQStoreMetadata
        {
            SchemaVersion = EfCoreDataMigrationPipeline.CurrentSchemaVersion + 1,
            DataVersion = TimeTickerChainRootDataMigration<TimeTickerEntity>.Version + 1,
            LastMigrationId = "future",
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<TickerQStoreVersionException>(() =>
            fixture.Pipeline.RunAsync(fixture.Context));

        Assert.Contains("newer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upgrade", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Store_upgrade_pipeline_is_registered_before_readiness_with_optional_schema_migration(bool autoMigrate)
    {
        var builder = new TickerQEfCoreOptionBuilder<TimeTickerEntity, CronTickerEntity>();
        if (autoMigrate) builder.AutoMigrateDatabase();
        builder.UseTickerQDbContext<TickerQ.EntityFrameworkCore.DbContextFactory.TickerQDbContext>(
            options => options.UseSqlite("Data Source=:memory:"));
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        builder.ConfigureServices(services);

        var prerequisites = services
            .Where(x => x.ServiceType == typeof(TickerQ.Utilities.Interfaces.ITickerQPersistencePrerequisiteBootstrapper))
            .Select(x => x.ImplementationType)
            .ToArray();
        if (autoMigrate)
            Assert.Equal(new[] { typeof(EfCoreAutoMigrateBootstrapper<
                TickerQ.EntityFrameworkCore.DbContextFactory.TickerQDbContext>) }, prerequisites);
        else
            Assert.Empty(prerequisites);

        var bootstrappers = services
            .Where(x => x.ServiceType == typeof(TickerQ.Utilities.Interfaces.ITickerQPersistenceBootstrapper))
            .Select(x => x.ImplementationType)
            .ToArray();
        Assert.Equal(typeof(EfCoreStoreUpgradeBootstrapper<
            TickerQ.EntityFrameworkCore.DbContextFactory.TickerQDbContext, TimeTickerEntity, CronTickerEntity>), bootstrappers[0]);
        Assert.Single(bootstrappers);

        var readinessProbes = services
            .Where(x => x.ServiceType == typeof(TickerQ.Utilities.Interfaces.ITickerQPersistenceReadinessProbe))
            .Select(x => x.ImplementationType)
            .ToArray();
        Assert.Equal(typeof(EfCoreNodeFinalizationOutboxReadinessProbe<
            TickerQ.EntityFrameworkCore.DbContextFactory.TickerQDbContext>), Assert.Single(readinessProbes));
    }

    [Fact]
    public async Task Missing_required_schema_names_the_consumer_migration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(connection).Options;
        await using var context = new TestTickerQDbContext(options);

        var pipeline = UpgradeFixture.CreatePipeline();
        var error = await Assert.ThrowsAsync<TickerQStoreSchemaException>(() => pipeline.RunAsync(context));

        Assert.Contains("TickerQStoreMetadata", error.Message);
        Assert.Contains("dotnet ef migrations add", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(TimeTickerChainRootDataMigration<TimeTickerEntity>.Id, error.Message);
    }

    [Fact]
    public async Task Partial_professional_schema_without_migrations_is_not_stamped_current()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(connection).Options;
        await using var context = new TestTickerQDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE NodeFinalizationOutbox");

        await Assert.ThrowsAsync<TickerQStoreSchemaException>(() =>
            UpgradeFixture.CreatePipeline().RunAsync(context));

        context.ChangeTracker.Clear();
        Assert.Empty(await context.Set<TickerQStoreMetadata>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Required_index_name_with_wrong_unique_semantics_is_not_certified()
    {
        await using var fixture = await UpgradeFixture.CreateAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("DROP INDEX UX_CronTickers_SeedKey");
        await fixture.Context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX UX_CronTickers_SeedKey ON CronTickers (SeedKey)");

        var error = await Assert.ThrowsAsync<TickerQStoreSchemaException>(() =>
            fixture.Pipeline.RunAsync(fixture.Context));

        Assert.Contains("UX_CronTickers_SeedKey", error.ToString());
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.Set<TickerQStoreMetadata>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Sixteen_concurrent_pipelines_converge_on_one_truthful_checkpoint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tickerq-pipeline-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite($"Data Source={path};Default Timeout=30;Pooling=False").Options;
        try
        {
            await using (var setup = new TestTickerQDbContext(options))
                await setup.Database.EnsureCreatedAsync();

            await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
            {
                await using var context = new TestTickerQDbContext(options);
                await UpgradeFixture.CreatePipeline().RunAsync(context);
            }));

            await using var verify = new TestTickerQDbContext(options);
            var row = await verify.Set<TickerQStoreMetadata>().AsNoTracking()
                .SingleAsync(x => x.Id == TickerQStoreMetadata.SingletonId);
            Assert.Equal(EfCoreDataMigrationPipeline.CurrentSchemaVersion, row.SchemaVersion);
            Assert.Equal(TimeTickerChainRootDataMigration<TimeTickerEntity>.Version, row.DataVersion);
            Assert.Equal(TimeTickerChainRootDataMigration<TimeTickerEntity>.Id, row.LastMigrationId);
            Assert.True(row.Version > 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Nested_serialization_failure_retries_migration_with_fresh_context_and_transaction()
    {
        await using var fixture = await UpgradeFixture.CreateAsync();
        var migration = new SerializationFaultOnceMigration();
        var pipeline = new EfCoreDataMigrationPipeline([migration]);

        await pipeline.RunAsync(fixture.Context);

        Assert.Equal(2, migration.ContextIds.Count);
        Assert.Equal(2, migration.ContextIds.Distinct().Count());
        var checkpoint = await fixture.Context.Set<TickerQStoreMetadata>().AsNoTracking()
            .SingleAsync(x => x.Id == TickerQStoreMetadata.SingletonId);
        Assert.Equal(TimeTickerChainRootDataMigration<TimeTickerEntity>.Version, checkpoint.DataVersion);
    }

    private sealed class SerializationFaultOnceMigration : ITickerQDataMigration
    {
        private readonly TimeTickerChainRootDataMigration<TimeTickerEntity> _inner = new();
        private int _attempts;
        public List<Guid> ContextIds { get; } = [];
        public string MigrationId => _inner.MigrationId;
        public int TargetDataVersion => _inner.TargetDataVersion;

        public async Task ApplyAsync(DbContext context, CancellationToken cancellationToken = default)
        {
            ContextIds.Add(context.ContextId.InstanceId);
            if (Interlocked.Increment(ref _attempts) == 1)
                throw new InvalidOperationException("nested", new SerializationDbException());
            await _inner.ApplyAsync(context, cancellationToken);
        }
    }

    private sealed class SerializationDbException : DbException
    {
        public override string SqlState => "40001";
    }

    private sealed class UpgradeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public TestTickerQDbContext Context { get; }
        public EfCoreDataMigrationPipeline Pipeline { get; } = CreatePipeline();

        private UpgradeFixture(SqliteConnection connection, TestTickerQDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public static async Task<UpgradeFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(connection).Options;
            var context = new TestTickerQDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new UpgradeFixture(connection, context);
        }

        public static EfCoreDataMigrationPipeline CreatePipeline() => new(
            [new TimeTickerChainRootDataMigration<TimeTickerEntity>()]);

        public TimeTickerEntity Ticker(TickerStatus status, Guid? parentId = null) => new()
        {
            Id = Guid.NewGuid(), Function = "upgrade", Request = [], Status = status,
            ParentId = parentId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
