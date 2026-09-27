using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

public enum TickerQMalformedGraphKind
{
    Orphan,
    Cycle
}

public sealed class TickerQDataMigrationException : InvalidOperationException
{
    public TickerQMalformedGraphKind GraphKind { get; }
    public Guid TickerId { get; }
    public Guid? ParentId { get; }

    internal TickerQDataMigrationException(
        TickerQMalformedGraphKind graphKind, Guid tickerId, Guid? parentId, string message)
        : base(message)
    {
        GraphKind = graphKind;
        TickerId = tickerId;
        ParentId = parentId;
    }
}

public sealed class TickerQStoreVersionException : InvalidOperationException
{
    internal TickerQStoreVersionException(string message) : base(message) { }
}

public sealed class TickerQStoreSchemaException : InvalidOperationException
{
    internal TickerQStoreSchemaException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>A provider-portable, idempotent data upgrade executed after schema migration.</summary>
public interface ITickerQDataMigration
{
    string MigrationId { get; }
    int TargetDataVersion { get; }
    Task ApplyAsync(DbContext context, CancellationToken cancellationToken = default);
}

public sealed class EfCoreDataMigrationPipeline
{
    public const int CurrentSchemaVersion = 1;
    private readonly IReadOnlyList<ITickerQDataMigration> _migrations;
    private readonly string _runtimePartitionKey;

    public EfCoreDataMigrationPipeline(
        IEnumerable<ITickerQDataMigration> migrations,
        TickerQRuntimePartition runtimePartition = null)
    {
        _runtimePartitionKey = (runtimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey;
        _migrations = (migrations ?? throw new ArgumentNullException(nameof(migrations)))
            .OrderBy(x => x.TargetDataVersion).ToArray();
        if (_migrations.Select(x => x.TargetDataVersion).Distinct().Count() != _migrations.Count)
            throw new ArgumentException("TickerQ data migration versions must be unique.", nameof(migrations));
    }

    public async Task RunAsync(DbContext context, CancellationToken cancellationToken = default)
        => await RunAsync(context, ct => Task.FromResult(CreateFreshContext(context)), cancellationToken)
            .ConfigureAwait(false);

    public async Task RunAsync(
        DbContext context,
        Func<CancellationToken, Task<DbContext>> attemptContextFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(attemptContextFactory);
        var requiredMigration = _migrations.LastOrDefault()?.MigrationId ?? "TickerQ EF schema baseline";
        try
        {
            // Migration history is the first schema proof. A baseline must never be stamped while the
            // consumer context still has unapplied migrations; AutoMigrate callers apply these first.
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (pendingMigrations.Any())
                throw new InvalidOperationException(
                    "The consumer DbContext has pending schema migrations: " +
                    string.Join(", ", pendingMigrations.Take(5)) + ".");

            await ProveProfessionalSchemaAsync(context, cancellationToken).ConfigureAwait(false);

            const int maxAttempts = 16;
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var attemptContext = await attemptContextFactory(cancellationToken)
                        .ConfigureAwait(false);
                    if (ReferenceEquals(attemptContext, context))
                        throw new InvalidOperationException(
                            "TickerQ data migration attempts require a fresh DbContext instance.");
                    await RunAttemptAsync(attemptContext, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (DbUpdateConcurrencyException) when (attempt + 1 < maxAttempts) { }
                catch (DbUpdateException) when (attempt + 1 < maxAttempts) { }
                catch (Exception ex) when (attempt + 1 < maxAttempts && IsSerializationFailure(ex)) { }

                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50, attempt + 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TickerQDataMigrationException)
        {
            throw;
        }
        catch (TickerQStoreVersionException)
        {
            throw;
        }
        catch (TickerQStoreSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TickerQStoreSchemaException(
                $"TickerQ EF store is missing the schema required by data migration '{requiredMigration}'. " +
                "Generate and deploy the consumer-owned migration (for example: " +
                "'dotnet ef migrations add TickerQProfessionalStoreUpgrade' then 'dotnet ef database update'). " +
                "The migration must include TickerQStoreMetadata, ChainRootId, NodeFinalizationOutbox, " +
                "and the current additive CronTicker columns/indexes.", ex);
        }
    }

    private static DbContext CreateFreshContext(DbContext template)
    {
        var options = template.GetService<IDbContextOptions>();
        var constructor = template.GetType().GetConstructors()
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(options);
            });
        if (constructor == null)
            throw new InvalidOperationException(
                $"DbContext '{template.GetType().Name}' cannot be recreated for a fresh migration attempt. " +
                "Use the RunAsync overload that supplies an attempt context factory.");
        return (DbContext)constructor.Invoke([options]);
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is DbException { SqlState: "40001" })
                return true;
        return false;
    }

    private static async Task ProveProfessionalSchemaAsync(DbContext context, CancellationToken cancellationToken)
    {
        var model = context.Model;
        var cron = model.GetEntityTypes().FirstOrDefault(x => typeof(CronTickerEntity).IsAssignableFrom(x.ClrType));
        var occurrence = model.GetEntityTypes().FirstOrDefault(x =>
            x.FindProperty("CronTickerId") != null && x.FindProperty("DefinitionRevision") != null);
        var time = model.GetEntityTypes().FirstOrDefault(x =>
            x.FindProperty("ParentId") != null && x.FindProperty("ChainRootId") != null &&
            !typeof(CronTickerEntity).IsAssignableFrom(x.ClrType));

        var required = new[]
        {
            Requirement(model.FindEntityType(typeof(TickerQStoreMetadata)),
                nameof(TickerQStoreMetadata.SchemaVersion), nameof(TickerQStoreMetadata.DataVersion),
                nameof(TickerQStoreMetadata.LastMigrationId), nameof(TickerQStoreMetadata.Version),
                nameof(TickerQStoreMetadata.ActivationEpoch), nameof(TickerQStoreMetadata.ActivationPhase),
                nameof(TickerQStoreMetadata.ActivationCheckpoint)),
            Requirement(model.FindEntityType(typeof(NodeFinalizationOutboxEntity)),
                typeof(NodeFinalizationOutboxEntity).GetProperties().Select(x => x.Name).ToArray()),
            Requirement(cron, "SeedKey", "SeedOwnerNamespace", "SeedManifestEpoch", "DefinitionRevision",
                "SeedLastSeenAt", "RetirementRequestedAt", "RetiredAt", "SeedWasEnabledBeforeRetirement"),
            Requirement(occurrence, "DefinitionRevision"),
            Requirement(time, "ChainRootId", "ChainGeneration")
        };

        var helper = context.GetService<ISqlGenerationHelper>();
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var item in required)
            {
                var store = StoreObjectIdentifier.Table(item.Entity.GetTableName()!, item.Entity.GetSchema());
                var columns = item.Properties.Select(x => x.GetColumnName(store)).ToArray();
                if (columns.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidOperationException($"TickerQ model mapping for '{item.Entity.ClrType.Name}' is incomplete.");

                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {string.Join(", ", columns.Select(helper.DelimitIdentifier))} " +
                    $"FROM {helper.DelimitIdentifier(item.Entity.GetTableName()!, item.Entity.GetSchema())} WHERE 1 = 0";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var providerName = context.Database.ProviderName ?? string.Empty;
            var provider = EfCoreRelationalIndexVerifier.DetectProvider(providerName);
            var requiredIndexes = required.SelectMany(item => item.Entity.GetIndexes()
                    .Where(IsProfessionalIndex)
                    .Select(index => IndexRequirement(item.Entity, index, provider)))
                .Distinct()
                .ToArray();
            await EfCoreRelationalIndexVerifier.VerifyAsync(
                    connection, providerName, requiredIndexes, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync().ConfigureAwait(false);
        }

        static (IEntityType Entity, IReadOnlyList<IProperty> Properties) Requirement(
            IEntityType entity, params string[] properties)
        {
            if (entity == null)
                throw new InvalidOperationException("A required TickerQ professional table is not mapped.");
            var mapped = properties.Select(name => entity.FindProperty(name) ??
                throw new InvalidOperationException(
                    $"Required TickerQ property '{entity.ClrType.Name}.{name}' is not mapped.")).ToArray();
            return (entity, mapped);
        }

        static RelationalIndexRequirement IndexRequirement(
            IEntityType entity,
            IIndex index,
            RelationalIndexProvider provider)
        {
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var name = index.GetDatabaseName() ??
                throw new InvalidOperationException(
                    $"Required TickerQ index on '{entity.ClrType.Name}' has no database name.");
            var columns = index.Properties.Select(property => property.GetColumnName(store) ??
                    throw new InvalidOperationException(
                        $"Required TickerQ index '{name}' property '{property.Name}' has no column mapping."))
                .ToArray();
            // SQLite has no schemas. MySQL's INFORMATION_SCHEMA TABLE_SCHEMA is the current database,
            // while EF relational schemas are unsupported; both are scoped by the catalog query itself.
            var schema = provider is RelationalIndexProvider.Sqlite or RelationalIndexProvider.MySql
                ? null
                : entity.GetSchema();
            var nullableUniqueSeedKey = index.IsUnique && index.Properties.Count == 1 &&
                                        index.Properties[0].Name == "SeedKey" && index.Properties[0].IsNullable;
            return new RelationalIndexRequirement(
                name, schema, entity.GetTableName()!, columns, index.IsUnique, nullableUniqueSeedKey);
        }

        static bool IsProfessionalIndex(IIndex index)
        {
            var names = index.Properties.Select(x => x.Name).ToArray();
            return names.Contains("SeedKey") || names.Contains("SeedOwnerNamespace") ||
                   names.Contains("DefinitionRevision") || names.Contains("ChainRootId") ||
                   names.Contains("AvailableAtUtc") || names.Contains("NodeEpoch");
        }
    }

    private async Task RunAttemptAsync(DbContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var metadataSet = context.Set<TickerQStoreMetadata>();
        if (!await metadataSet.AsNoTracking().AnyAsync(
                x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                     x.Id == TickerQStoreMetadata.TimeTickerGraphMutationSentinelId,
                cancellationToken).ConfigureAwait(false))
        {
            metadataSet.Add(new TickerQStoreMetadata
            {
                ApplicationNamespaceKey = _runtimePartitionKey,
                Id = TickerQStoreMetadata.TimeTickerGraphMutationSentinelId,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        var sentinelLocked = await metadataSet
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.Id == TickerQStoreMetadata.TimeTickerGraphMutationSentinelId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, x => x.Version),
                cancellationToken).ConfigureAwait(false);
        if (sentinelLocked != 1)
            throw new DbUpdateConcurrencyException(
                "TickerQ data migration could not acquire the TimeTicker graph mutation sentinel.");

        var metadata = await metadataSet
            .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                       x.Id == TickerQStoreMetadata.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        var currentDataVersion = _migrations.LastOrDefault()?.TargetDataVersion ?? 0;
        if (metadata != null &&
            (metadata.SchemaVersion > CurrentSchemaVersion || metadata.DataVersion > currentDataVersion))
        {
            throw new TickerQStoreVersionException(
                $"TickerQ store version schema={metadata.SchemaVersion}, data={metadata.DataVersion} is newer " +
                $"than this binary supports (schema={CurrentSchemaVersion}, data={currentDataVersion}). " +
                "Upgrade the TickerQ application before opening this store.");
        }

        metadata ??= new TickerQStoreMetadata
        {
            ApplicationNamespaceKey = _runtimePartitionKey,
            Id = TickerQStoreMetadata.SingletonId,
            SchemaVersion = CurrentSchemaVersion,
            DataVersion = 0,
            Version = 1,
            UpdatedAtUtc = DateTime.UtcNow
        };
        if (context.Entry(metadata).State == EntityState.Detached)
            context.Add(metadata);

        foreach (var migration in _migrations.Where(x => x.TargetDataVersion > metadata.DataVersion))
        {
            // ApplyAsync must prove its required table/column shape through real provider queries and
            // postconditions before this durable checkpoint is written in the same transaction.
            await migration.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
            metadata.SchemaVersion = CurrentSchemaVersion;
            metadata.DataVersion = migration.TargetDataVersion;
            metadata.LastMigrationId = migration.MigrationId;
            metadata.UpdatedAtUtc = DateTime.UtcNow;
            if (context.Entry(metadata).State != EntityState.Added)
                metadata.Version++;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (context.Entry(metadata).State == EntityState.Added)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Commit is deliberately non-cancellable after every cancellable migration/write completed.
        // Once this returns, callers must observe success rather than a false cancellation report.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

public sealed class TimeTickerChainRootDataMigration<TTimeTicker> : ITickerQDataMigration
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
{
    private readonly string _runtimePartitionKey;

    public TimeTickerChainRootDataMigration(TickerQRuntimePartition runtimePartition = null)
        => _runtimePartitionKey = (runtimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey;

    public const int Version = 1;
    public const string Id = "2026-07-TimeTicker-ChainRoot-Backfill-v1";
    public string MigrationId => Id;
    public int TargetDataVersion => Version;

    public async Task ApplyAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var rows = await context.Set<TTimeTicker>().AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
            .Select(x => new GraphRow(x.Id, x.ParentId, x.ChainRootId))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var byId = rows.ToDictionary(x => x.Id);
        var resolved = new Dictionary<Guid, Guid>(rows.Length);
        var visiting = new HashSet<Guid>();

        Guid Resolve(GraphRow row)
        {
            if (resolved.TryGetValue(row.Id, out var known)) return known;
            if (!visiting.Add(row.Id))
                throw new TickerQDataMigrationException(
                    TickerQMalformedGraphKind.Cycle, row.Id, row.ParentId,
                    $"TickerQ TimeTicker graph contains a cycle at ticker '{row.Id}' (ParentId '{row.ParentId}'). " +
                    "Break the ParentId cycle and restart the data migration.");

            Guid root;
            if (!row.ParentId.HasValue)
            {
                root = row.Id;
            }
            else if (!byId.TryGetValue(row.ParentId.Value, out var parent))
            {
                throw new TickerQDataMigrationException(
                    TickerQMalformedGraphKind.Orphan, row.Id, row.ParentId,
                    $"TickerQ TimeTicker '{row.Id}' has ParentId '{row.ParentId}' but that parent does not exist. " +
                    "Restore the parent or clear/correct ParentId, then restart the data migration.");
            }
            else
            {
                root = Resolve(parent);
            }

            visiting.Remove(row.Id);
            resolved[row.Id] = root;
            return root;
        }

        foreach (var row in rows) Resolve(row);

        const int batchSize = 500;
        foreach (var group in rows.Where(x => x.ChainRootId != resolved[x.Id]).GroupBy(x => resolved[x.Id]))
        {
            var ids = group.Select(x => x.Id).ToArray();
            for (var offset = 0; offset < ids.Length; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = ids.Skip(offset).Take(batchSize).ToArray();
                var rootId = group.Key;
                await context.Set<TTimeTicker>().Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                                            batch.Contains(x.Id))
                    .ExecuteUpdateAsync(setter => setter.SetProperty(x => x.ChainRootId, rootId), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var postcondition = await context.Set<TTimeTicker>().AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
            .Select(x => new GraphRow(x.Id, x.ParentId, x.ChainRootId))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var invalid = postcondition.FirstOrDefault(x => !x.ChainRootId.HasValue || x.ChainRootId != resolved[x.Id]);
        if (invalid != null)
            throw new InvalidOperationException(
                $"TickerQ TimeTicker ChainRootId postcondition failed for ticker '{invalid.Id}'.");
    }

    private sealed record GraphRow(Guid Id, Guid? ParentId, Guid? ChainRootId);
}
