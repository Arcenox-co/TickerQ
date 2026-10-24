using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// RelationalInsertIfMissing replaced FlexLabs.EntityFrameworkCore.Upsert for TickerQ's two
/// insert-if-missing statements. The golden data below is the exact SQL and parameter list FlexLabs
/// 10.0.0 generated for those statements, captured before the package was removed; any change to the
/// generated statements fails these tests. The relational conformance suite runs the same statements
/// against real servers.
/// </summary>
public class InsertIfMissingTests
{
    private static readonly string[] SentinelKeys =
        [nameof(TickerQStoreMetadata.ApplicationNamespaceKey), nameof(TickerQStoreMetadata.Id)];

    private static readonly string[] OccurrenceKeys =
    [
        nameof(CronTickerOccurrenceEntity<CronTickerEntity>.ApplicationNamespaceKey),
        nameof(CronTickerOccurrenceEntity<CronTickerEntity>.ExecutionTime),
        nameof(CronTickerOccurrenceEntity<CronTickerEntity>.CronTickerId),
    ];

    public static TheoryData<string, string, string, string[]> FlexLabsGolden => new()
    {
        { "mysql", "occurrence", "INSERT IGNORE INTO `ticker`.`CronTickerOccurrences` (`ApplicationNamespaceKey`, `Id`, `AcquisitionToken`, `ChainRootId`, `CreatedAt`, `CronTickerId`, `DefinitionRevision`, `ElapsedTime`, `ExceptionMessage`, `ExecutedAt`, `ExecutionTime`, `LeaseUntil`, `LockHolder`, `LockedAt`, `ProgressMessage`, `ProgressPercent`, `ProgressUpdatedAt`, `RetryCount`, `SkippedReason`, `StaleRestartCount`, `Status`, `UpdatedAt`) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21)",
            ["@p0|tq:runtime:v1:abc|String|80|MySqlParameter", "@p1|11111111-2222-3333-4444-555555555555|Guid|0|MySqlParameter", "@p2|aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|Guid|0|MySqlParameter", "@p3|<dbnull>|Guid|0|MySqlParameter", "@p4|2026-10-24T12:00:01.0000000Z|DateTime|0|MySqlParameter", "@p5|66666666-7777-8888-9999-000000000000|Guid|0|MySqlParameter", "@p6|3|Int64|0|MySqlParameter", "@p7|0|Int64|0|MySqlParameter", "@p8|<dbnull>|String|4000|MySqlParameter", "@p9|<dbnull>|DateTime|0|MySqlParameter", "@p10|2026-10-24T12:05:00.0000000Z|DateTime|0|MySqlParameter", "@p11|<dbnull>|DateTime|0|MySqlParameter", "@p12|node-1|String|4000|MySqlParameter", "@p13|2026-10-24T12:00:01.0000000Z|DateTime|0|MySqlParameter", "@p14|<dbnull>|String|500|MySqlParameter", "@p15|<dbnull>|Double|0|MySqlParameter", "@p16|<dbnull>|DateTime|0|MySqlParameter", "@p17|0|Int32|0|MySqlParameter", "@p18|<dbnull>|String|4000|MySqlParameter", "@p19|0|Int32|0|MySqlParameter", "@p20|1|Int32|0|MySqlParameter", "@p21|2026-10-24T12:00:01.0000000Z|DateTime|0|MySqlParameter"] },
        { "mysql", "sentinel", "INSERT IGNORE INTO `ticker`.`TickerQStoreMetadata` (`ApplicationNamespaceKey`, `Id`, `ActivationCheckpoint`, `ActivationEpoch`, `ActivationManifest`, `ActivationManifestHistory`, `ActivationPhase`, `DataVersion`, `LastMigrationId`, `SchemaVersion`, `UpdatedAtUtc`, `Version`) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11)",
            ["@p0|tq:runtime:v1:abc|String|80|MySqlParameter", "@p1|TickerQ:TimeTickerGraphMutation|String|80|MySqlParameter", "@p2|<dbnull>|String|200|MySqlParameter", "@p3|0|Int64|0|MySqlParameter", "@p4|<dbnull>|String|48|MySqlParameter", "@p5|<dbnull>|String|1568|MySqlParameter", "@p6|0|Int32|0|MySqlParameter", "@p7|0|Int32|0|MySqlParameter", "@p8|<dbnull>|String|200|MySqlParameter", "@p9|0|Int32|0|MySqlParameter", "@p10|2026-10-24T12:00:00.0000000Z|DateTime|0|MySqlParameter", "@p11|0|Int64|0|MySqlParameter"] },
        { "oracle", "occurrence", "MERGE INTO \"ticker\".\"CronTickerOccurrences\" t USING (SELECT :p0 AS \"ApplicationNamespaceKey\", :p1 AS \"Id\", :p2 AS \"AcquisitionToken\", :p3 AS \"ChainRootId\", :p4 AS \"CreatedAt\", :p5 AS \"CronTickerId\", :p6 AS \"DefinitionRevision\", :p7 AS \"ElapsedTime\", :p8 AS \"ExceptionMessage\", :p9 AS \"ExecutedAt\", :p10 AS \"ExecutionTime\", :p11 AS \"LeaseUntil\", :p12 AS \"LockHolder\", :p13 AS \"LockedAt\", :p14 AS \"ProgressMessage\", :p15 AS \"ProgressPercent\", :p16 AS \"ProgressUpdatedAt\", :p17 AS \"RetryCount\", :p18 AS \"SkippedReason\", :p19 AS \"StaleRestartCount\", :p20 AS \"Status\", :p21 AS \"UpdatedAt\" FROM dual) s ON (t.\"ApplicationNamespaceKey\" = s.\"ApplicationNamespaceKey\" AND t.\"ExecutionTime\" = s.\"ExecutionTime\" AND t.\"CronTickerId\" = s.\"CronTickerId\") WHEN NOT MATCHED THEN INSERT (\"ApplicationNamespaceKey\", \"Id\", \"AcquisitionToken\", \"ChainRootId\", \"CreatedAt\", \"CronTickerId\", \"DefinitionRevision\", \"ElapsedTime\", \"ExceptionMessage\", \"ExecutedAt\", \"ExecutionTime\", \"LeaseUntil\", \"LockHolder\", \"LockedAt\", \"ProgressMessage\", \"ProgressPercent\", \"ProgressUpdatedAt\", \"RetryCount\", \"SkippedReason\", \"StaleRestartCount\", \"Status\", \"UpdatedAt\") VALUES (s.\"ApplicationNamespaceKey\", s.\"Id\", s.\"AcquisitionToken\", s.\"ChainRootId\", s.\"CreatedAt\", s.\"CronTickerId\", s.\"DefinitionRevision\", s.\"ElapsedTime\", s.\"ExceptionMessage\", s.\"ExecutedAt\", s.\"ExecutionTime\", s.\"LeaseUntil\", s.\"LockHolder\", s.\"LockedAt\", s.\"ProgressMessage\", s.\"ProgressPercent\", s.\"ProgressUpdatedAt\", s.\"RetryCount\", s.\"SkippedReason\", s.\"StaleRestartCount\", s.\"Status\", s.\"UpdatedAt\")",
            [":p0|tq:runtime:v1:abc|String|80|OracleParameter", ":p1|11111111-2222-3333-4444-555555555555|Binary|0|OracleParameter", ":p2|aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|Binary|0|OracleParameter", ":p3|<dbnull>|Binary|0|OracleParameter", ":p4|2026-10-24T12:00:01.0000000Z|DateTime|0|OracleParameter", ":p5|66666666-7777-8888-9999-000000000000|Binary|0|OracleParameter", ":p6|3|Int64|0|OracleParameter", ":p7|0|Int64|0|OracleParameter", ":p8|<dbnull>|String|2000|OracleParameter", ":p9|<dbnull>|DateTime|0|OracleParameter", ":p10|2026-10-24T12:05:00.0000000Z|DateTime|0|OracleParameter", ":p11|<dbnull>|DateTime|0|OracleParameter", ":p12|node-1|String|2000|OracleParameter", ":p13|2026-10-24T12:00:01.0000000Z|DateTime|0|OracleParameter", ":p14|<dbnull>|String|500|OracleParameter", ":p15|<dbnull>|Double|0|OracleParameter", ":p16|<dbnull>|DateTime|0|OracleParameter", ":p17|0|Int32|0|OracleParameter", ":p18|<dbnull>|String|2000|OracleParameter", ":p19|0|Int32|0|OracleParameter", ":p20|1|Int32|0|OracleParameter", ":p21|2026-10-24T12:00:01.0000000Z|DateTime|0|OracleParameter"] },
        { "oracle", "sentinel", "MERGE INTO \"ticker\".\"TickerQStoreMetadata\" t USING (SELECT :p0 AS \"ApplicationNamespaceKey\", :p1 AS \"Id\", :p2 AS \"ActivationCheckpoint\", :p3 AS \"ActivationEpoch\", :p4 AS \"ActivationManifest\", :p5 AS \"ActivationManifestHistory\", :p6 AS \"ActivationPhase\", :p7 AS \"DataVersion\", :p8 AS \"LastMigrationId\", :p9 AS \"SchemaVersion\", :p10 AS \"UpdatedAtUtc\", :p11 AS \"Version\" FROM dual) s ON (t.\"ApplicationNamespaceKey\" = s.\"ApplicationNamespaceKey\" AND t.\"Id\" = s.\"Id\") WHEN NOT MATCHED THEN INSERT (\"ApplicationNamespaceKey\", \"Id\", \"ActivationCheckpoint\", \"ActivationEpoch\", \"ActivationManifest\", \"ActivationManifestHistory\", \"ActivationPhase\", \"DataVersion\", \"LastMigrationId\", \"SchemaVersion\", \"UpdatedAtUtc\", \"Version\") VALUES (s.\"ApplicationNamespaceKey\", s.\"Id\", s.\"ActivationCheckpoint\", s.\"ActivationEpoch\", s.\"ActivationManifest\", s.\"ActivationManifestHistory\", s.\"ActivationPhase\", s.\"DataVersion\", s.\"LastMigrationId\", s.\"SchemaVersion\", s.\"UpdatedAtUtc\", s.\"Version\")",
            [":p0|tq:runtime:v1:abc|String|80|OracleParameter", ":p1|TickerQ:TimeTickerGraphMutation|String|80|OracleParameter", ":p2|<dbnull>|String|200|OracleParameter", ":p3|0|Int64|0|OracleParameter", ":p4|<dbnull>|String|48|OracleParameter", ":p5|<dbnull>|String|1568|OracleParameter", ":p6|0|Int32|0|OracleParameter", ":p7|0|Int32|0|OracleParameter", ":p8|<dbnull>|String|200|OracleParameter", ":p9|0|Int32|0|OracleParameter", ":p10|2026-10-24T12:00:00.0000000Z|DateTime|0|OracleParameter", ":p11|0|Int64|0|OracleParameter"] },
        { "postgres", "occurrence", "INSERT INTO \"ticker\".\"CronTickerOccurrences\" AS \"T\" (\"ApplicationNamespaceKey\", \"Id\", \"AcquisitionToken\", \"ChainRootId\", \"CreatedAt\", \"CronTickerId\", \"DefinitionRevision\", \"ElapsedTime\", \"ExceptionMessage\", \"ExecutedAt\", \"ExecutionTime\", \"LeaseUntil\", \"LockHolder\", \"LockedAt\", \"ProgressMessage\", \"ProgressPercent\", \"ProgressUpdatedAt\", \"RetryCount\", \"SkippedReason\", \"StaleRestartCount\", \"Status\", \"UpdatedAt\") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21) ON CONFLICT (\"ApplicationNamespaceKey\", \"ExecutionTime\", \"CronTickerId\") DO NOTHING",
            ["@p0|tq:runtime:v1:abc|String|0|NpgsqlParameter", "@p1|11111111-2222-3333-4444-555555555555|Guid|0|NpgsqlParameter", "@p2|aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|Guid|0|NpgsqlParameter", "@p3|<dbnull>|Guid|0|NpgsqlParameter", "@p4|2026-10-24T12:00:01.0000000Z|DateTime|0|NpgsqlParameter", "@p5|66666666-7777-8888-9999-000000000000|Guid|0|NpgsqlParameter", "@p6|3|Int64|0|NpgsqlParameter", "@p7|0|Int64|0|NpgsqlParameter", "@p8|<dbnull>|String|0|NpgsqlParameter", "@p9|<dbnull>|DateTime|0|NpgsqlParameter", "@p10|2026-10-24T12:05:00.0000000Z|DateTime|0|NpgsqlParameter", "@p11|<dbnull>|DateTime|0|NpgsqlParameter", "@p12|node-1|String|0|NpgsqlParameter", "@p13|2026-10-24T12:00:01.0000000Z|DateTime|0|NpgsqlParameter", "@p14|<dbnull>|String|0|NpgsqlParameter", "@p15|<dbnull>|Double|0|NpgsqlParameter", "@p16|<dbnull>|DateTime|0|NpgsqlParameter", "@p17|0|Int32|0|NpgsqlParameter", "@p18|<dbnull>|String|0|NpgsqlParameter", "@p19|0|Int32|0|NpgsqlParameter", "@p20|1|Int32|0|NpgsqlParameter", "@p21|2026-10-24T12:00:01.0000000Z|DateTime|0|NpgsqlParameter"] },
        { "postgres", "sentinel", "INSERT INTO \"ticker\".\"TickerQStoreMetadata\" AS \"T\" (\"ApplicationNamespaceKey\", \"Id\", \"ActivationCheckpoint\", \"ActivationEpoch\", \"ActivationManifest\", \"ActivationManifestHistory\", \"ActivationPhase\", \"DataVersion\", \"LastMigrationId\", \"SchemaVersion\", \"UpdatedAtUtc\", \"Version\") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11) ON CONFLICT (\"ApplicationNamespaceKey\", \"Id\") DO NOTHING",
            ["@p0|tq:runtime:v1:abc|String|0|NpgsqlParameter", "@p1|TickerQ:TimeTickerGraphMutation|String|0|NpgsqlParameter", "@p2|<dbnull>|String|0|NpgsqlParameter", "@p3|0|Int64|0|NpgsqlParameter", "@p4|<dbnull>|String|0|NpgsqlParameter", "@p5|<dbnull>|String|0|NpgsqlParameter", "@p6|0|Int32|0|NpgsqlParameter", "@p7|0|Int32|0|NpgsqlParameter", "@p8|<dbnull>|String|0|NpgsqlParameter", "@p9|0|Int32|0|NpgsqlParameter", "@p10|2026-10-24T12:00:00.0000000Z|DateTime|0|NpgsqlParameter", "@p11|0|Int64|0|NpgsqlParameter"] },
        { "sqlite", "occurrence", "INSERT INTO \"CronTickerOccurrences\" AS \"T\" (\"ApplicationNamespaceKey\", \"Id\", \"AcquisitionToken\", \"ChainRootId\", \"CreatedAt\", \"CronTickerId\", \"DefinitionRevision\", \"ElapsedTime\", \"ExceptionMessage\", \"ExecutedAt\", \"ExecutionTime\", \"LeaseUntil\", \"LockHolder\", \"LockedAt\", \"ProgressMessage\", \"ProgressPercent\", \"ProgressUpdatedAt\", \"RetryCount\", \"SkippedReason\", \"StaleRestartCount\", \"Status\", \"UpdatedAt\") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21) ON CONFLICT (\"ApplicationNamespaceKey\", \"ExecutionTime\", \"CronTickerId\") DO NOTHING",
            ["@p0|tq:runtime:v1:abc|String|17|SqliteParameter", "@p1|11111111-2222-3333-4444-555555555555|Guid|0|SqliteParameter", "@p2|aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|Guid|0|SqliteParameter", "@p3|<dbnull>|Guid|0|SqliteParameter", "@p4|2026-10-24T12:00:01.0000000Z|DateTime|0|SqliteParameter", "@p5|66666666-7777-8888-9999-000000000000|Guid|0|SqliteParameter", "@p6|3|Int64|0|SqliteParameter", "@p7|0|Int64|0|SqliteParameter", "@p8|<dbnull>|String|0|SqliteParameter", "@p9|<dbnull>|DateTime|0|SqliteParameter", "@p10|2026-10-24T12:05:00.0000000Z|DateTime|0|SqliteParameter", "@p11|<dbnull>|DateTime|0|SqliteParameter", "@p12|node-1|String|6|SqliteParameter", "@p13|2026-10-24T12:00:01.0000000Z|DateTime|0|SqliteParameter", "@p14|<dbnull>|String|0|SqliteParameter", "@p15|<dbnull>|Double|0|SqliteParameter", "@p16|<dbnull>|DateTime|0|SqliteParameter", "@p17|0|Int32|0|SqliteParameter", "@p18|<dbnull>|String|0|SqliteParameter", "@p19|0|Int32|0|SqliteParameter", "@p20|1|Int32|0|SqliteParameter", "@p21|2026-10-24T12:00:01.0000000Z|DateTime|0|SqliteParameter"] },
        { "sqlite", "sentinel", "INSERT INTO \"TickerQStoreMetadata\" AS \"T\" (\"ApplicationNamespaceKey\", \"Id\", \"ActivationCheckpoint\", \"ActivationEpoch\", \"ActivationManifest\", \"ActivationManifestHistory\", \"ActivationPhase\", \"DataVersion\", \"LastMigrationId\", \"SchemaVersion\", \"UpdatedAtUtc\", \"Version\") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11) ON CONFLICT (\"ApplicationNamespaceKey\", \"Id\") DO NOTHING",
            ["@p0|tq:runtime:v1:abc|String|17|SqliteParameter", "@p1|TickerQ:TimeTickerGraphMutation|String|31|SqliteParameter", "@p2|<dbnull>|String|0|SqliteParameter", "@p3|0|Int64|0|SqliteParameter", "@p4|<dbnull>|String|0|SqliteParameter", "@p5|<dbnull>|String|0|SqliteParameter", "@p6|0|Int32|0|SqliteParameter", "@p7|0|Int32|0|SqliteParameter", "@p8|<dbnull>|String|0|SqliteParameter", "@p9|0|Int32|0|SqliteParameter", "@p10|2026-10-24T12:00:00.0000000Z|DateTime|0|SqliteParameter", "@p11|0|Int64|0|SqliteParameter"] },
        { "sqlserver", "occurrence", "MERGE INTO [ticker].[CronTickerOccurrences] WITH (HOLDLOCK) AS [T] USING ( VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21) ) AS [S] ([ApplicationNamespaceKey], [Id], [AcquisitionToken], [ChainRootId], [CreatedAt], [CronTickerId], [DefinitionRevision], [ElapsedTime], [ExceptionMessage], [ExecutedAt], [ExecutionTime], [LeaseUntil], [LockHolder], [LockedAt], [ProgressMessage], [ProgressPercent], [ProgressUpdatedAt], [RetryCount], [SkippedReason], [StaleRestartCount], [Status], [UpdatedAt]) ON [T].[ApplicationNamespaceKey] = [S].[ApplicationNamespaceKey] AND [T].[ExecutionTime] = [S].[ExecutionTime] AND [T].[CronTickerId] = [S].[CronTickerId] WHEN NOT MATCHED BY TARGET THEN INSERT ([ApplicationNamespaceKey], [Id], [AcquisitionToken], [ChainRootId], [CreatedAt], [CronTickerId], [DefinitionRevision], [ElapsedTime], [ExceptionMessage], [ExecutedAt], [ExecutionTime], [LeaseUntil], [LockHolder], [LockedAt], [ProgressMessage], [ProgressPercent], [ProgressUpdatedAt], [RetryCount], [SkippedReason], [StaleRestartCount], [Status], [UpdatedAt]) VALUES ([ApplicationNamespaceKey], [Id], [AcquisitionToken], [ChainRootId], [CreatedAt], [CronTickerId], [DefinitionRevision], [ElapsedTime], [ExceptionMessage], [ExecutedAt], [ExecutionTime], [LeaseUntil], [LockHolder], [LockedAt], [ProgressMessage], [ProgressPercent], [ProgressUpdatedAt], [RetryCount], [SkippedReason], [StaleRestartCount], [Status], [UpdatedAt]);",
            ["@p0|tq:runtime:v1:abc|String|80|SqlParameter", "@p1|11111111-2222-3333-4444-555555555555|Guid|0|SqlParameter", "@p2|aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|Guid|0|SqlParameter", "@p3|<dbnull>|Guid|0|SqlParameter", "@p4|2026-10-24T12:00:01.0000000Z|DateTime2|0|SqlParameter", "@p5|66666666-7777-8888-9999-000000000000|Guid|0|SqlParameter", "@p6|3|Int64|0|SqlParameter", "@p7|0|Int64|0|SqlParameter", "@p8|<dbnull>|String|4000|SqlParameter", "@p9|<dbnull>|DateTime2|0|SqlParameter", "@p10|2026-10-24T12:05:00.0000000Z|DateTime2|0|SqlParameter", "@p11|<dbnull>|DateTime2|0|SqlParameter", "@p12|node-1|String|4000|SqlParameter", "@p13|2026-10-24T12:00:01.0000000Z|DateTime2|0|SqlParameter", "@p14|<dbnull>|String|500|SqlParameter", "@p15|<dbnull>|Double|0|SqlParameter", "@p16|<dbnull>|DateTime2|0|SqlParameter", "@p17|0|Int32|0|SqlParameter", "@p18|<dbnull>|String|4000|SqlParameter", "@p19|0|Int32|0|SqlParameter", "@p20|1|Int32|0|SqlParameter", "@p21|2026-10-24T12:00:01.0000000Z|DateTime2|0|SqlParameter"] },
        { "sqlserver", "sentinel", "MERGE INTO [ticker].[TickerQStoreMetadata] WITH (HOLDLOCK) AS [T] USING ( VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11) ) AS [S] ([ApplicationNamespaceKey], [Id], [ActivationCheckpoint], [ActivationEpoch], [ActivationManifest], [ActivationManifestHistory], [ActivationPhase], [DataVersion], [LastMigrationId], [SchemaVersion], [UpdatedAtUtc], [Version]) ON [T].[ApplicationNamespaceKey] = [S].[ApplicationNamespaceKey] AND [T].[Id] = [S].[Id] WHEN NOT MATCHED BY TARGET THEN INSERT ([ApplicationNamespaceKey], [Id], [ActivationCheckpoint], [ActivationEpoch], [ActivationManifest], [ActivationManifestHistory], [ActivationPhase], [DataVersion], [LastMigrationId], [SchemaVersion], [UpdatedAtUtc], [Version]) VALUES ([ApplicationNamespaceKey], [Id], [ActivationCheckpoint], [ActivationEpoch], [ActivationManifest], [ActivationManifestHistory], [ActivationPhase], [DataVersion], [LastMigrationId], [SchemaVersion], [UpdatedAtUtc], [Version]);",
            ["@p0|tq:runtime:v1:abc|String|80|SqlParameter", "@p1|TickerQ:TimeTickerGraphMutation|String|80|SqlParameter", "@p2|<dbnull>|String|200|SqlParameter", "@p3|0|Int64|0|SqlParameter", "@p4|<dbnull>|String|48|SqlParameter", "@p5|<dbnull>|String|1568|SqlParameter", "@p6|0|Int32|0|SqlParameter", "@p7|0|Int32|0|SqlParameter", "@p8|<dbnull>|String|200|SqlParameter", "@p9|0|Int32|0|SqlParameter", "@p10|2026-10-24T12:00:00.0000000Z|DateTime2|0|SqlParameter", "@p11|0|Int64|0|SqlParameter"] },
    };

    [Theory]
    [MemberData(nameof(FlexLabsGolden))]
    public async Task Generates_the_same_sql_and_parameters_as_flexlabs(string provider, string statement,
        string expectedSql, string[] expectedParameters)
    {
        var captured = await CaptureAsync(provider, db => statement == "sentinel"
            ? RelationalInsertIfMissing.ExecuteAsync(db, Sentinel(), SentinelKeys, default)
            : RelationalInsertIfMissing.ExecuteAsync(db, Occurrence(), OccurrenceKeys, default));

        Assert.Equal(expectedSql, captured.Sql);
        Assert.Equal(expectedParameters, captured.Parameters);
    }

    [Fact]
    public async Task Inserts_a_missing_row_and_leaves_an_existing_row_unchanged()
    {
        await using var store = await SqliteStore.CreateAsync();
        await using var context = store.Context();

        Assert.Equal(1, await RelationalInsertIfMissing.ExecuteAsync(context, Sentinel(), SentinelKeys, default));

        var conflicting = Sentinel();
        conflicting.UpdatedAtUtc = conflicting.UpdatedAtUtc.AddDays(1);
        conflicting.Version = 99;
        Assert.Equal(0, await RelationalInsertIfMissing.ExecuteAsync(context, conflicting, SentinelKeys, default));

        var stored = await store.Context().Set<TickerQStoreMetadata>().AsNoTracking().SingleAsync();
        Assert.Equal(Sentinel().UpdatedAtUtc, stored.UpdatedAtUtc);
        Assert.Equal(0, stored.Version);
    }

    [Fact]
    public async Task Duplicate_occurrence_slot_is_ignored_even_with_a_different_id()
    {
        await using var store = await SqliteStore.CreateAsync();
        await using (var parent = store.Context())
        {
            // The occurrence references its cron definition.
            parent.Add(new CronTickerEntity
            {
                ApplicationNamespaceKey = Occurrence().ApplicationNamespaceKey,
                Id = Occurrence().CronTickerId,
                Function = "conformance.cron",
                Expression = "0 */5 * * * *",
                Request = [],
            });
            await parent.SaveChangesAsync();
        }
        await using var context = store.Context();

        Assert.Equal(1, await RelationalInsertIfMissing.ExecuteAsync(context, Occurrence(), OccurrenceKeys, default));
        var sameSlot = Occurrence();
        sameSlot.Id = Guid.NewGuid();
        Assert.Equal(0, await RelationalInsertIfMissing.ExecuteAsync(context, sameSlot, OccurrenceKeys, default));

        var stored = await store.Context().Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AsNoTracking().SingleAsync();
        Assert.Equal(Occurrence().Id, stored.Id);
        Assert.Equal(TickerStatus.Queued, stored.Status);
        Assert.Equal("node-1", stored.LockHolder);
    }

    [Fact]
    public async Task Does_not_flush_or_change_tracked_entities()
    {
        await using var store = await SqliteStore.CreateAsync();
        await using var context = store.Context();
        var pending = new TimeTickerEntity { Function = "pending", ExecutionTime = DateTime.UtcNow };
        context.Add(pending);

        Assert.Equal(1, await RelationalInsertIfMissing.ExecuteAsync(context, Sentinel(), SentinelKeys, default));

        Assert.Equal(EntityState.Added, context.Entry(pending).State);
        Assert.Empty(context.ChangeTracker.Entries<TickerQStoreMetadata>());
        Assert.Equal(0, await store.Context().Set<TimeTickerEntity>().CountAsync());
    }

    [Fact]
    public async Task Runs_inside_the_current_transaction()
    {
        await using var store = await SqliteStore.CreateAsync();
        await using var context = store.Context();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await RelationalInsertIfMissing.ExecuteAsync(context, Sentinel(), SentinelKeys, default));
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await store.Context().Set<TickerQStoreMetadata>().CountAsync());
    }

    [Theory]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", "PostgreSql")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", "Sqlite")]
    [InlineData("MySql.EntityFrameworkCore", "MySql")]
    [InlineData("MySql.Data.EntityFrameworkCore", "MySql")]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", "MySql")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "SqlServer")]
    [InlineData("Oracle.EntityFrameworkCore", "Oracle")]
    public void Selects_the_dialect_by_exact_provider_name(string providerName, string expectedDialect)
        => Assert.Equal(expectedDialect, RelationalInsertIfMissing.DialectFor(providerName).ToString());

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory")]
    [InlineData("Some.Other.Provider")]
    [InlineData(null)]
    public void Rejects_unknown_providers(string providerName)
        => Assert.Throws<NotSupportedException>(() => RelationalInsertIfMissing.DialectFor(providerName));

    [Fact]
    public void Rejects_missing_or_unknown_key_properties()
    {
        using var context = new TickerQDbContext(new DbContextOptionsBuilder<TickerQDbContext>().UseSqlite("Data Source=:memory:").Options);
        var entityType = context.Model.FindEntityType(typeof(TickerQStoreMetadata))!;

        Assert.Throws<ArgumentException>(() =>
            RelationalInsertIfMissing.Build(entityType, Sentinel(), [], RelationalInsertIfMissing.Dialect.Sqlite));
        Assert.Throws<InvalidOperationException>(() =>
            RelationalInsertIfMissing.Build(entityType, Sentinel(), ["NoSuchProperty"], RelationalInsertIfMissing.Dialect.Sqlite));
    }

    private static TickerQStoreMetadata Sentinel() => new()
    {
        ApplicationNamespaceKey = "tq:runtime:v1:abc",
        Id = TickerQStoreMetadata.TimeTickerGraphMutationSentinelId,
        UpdatedAtUtc = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
    };

    private static CronTickerOccurrenceEntity<CronTickerEntity> Occurrence() => new()
    {
        ApplicationNamespaceKey = "tq:runtime:v1:abc",
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Status = TickerStatus.Queued,
        LockHolder = "node-1",
        ExecutionTime = new DateTime(2026, 10, 24, 12, 5, 0, DateTimeKind.Utc),
        CronTickerId = Guid.Parse("66666666-7777-8888-9999-000000000000"),
        DefinitionRevision = 3,
        LockedAt = new DateTime(2026, 10, 24, 12, 0, 1, DateTimeKind.Utc),
        AcquisitionToken = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        CreatedAt = new DateTime(2026, 10, 24, 12, 0, 1, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 10, 24, 12, 0, 1, DateTimeKind.Utc),
    };

    private sealed record Captured(string Sql, string[] Parameters);

    /// <summary>Builds the statement for a provider without a server: opening and execution are suppressed.</summary>
    private static async Task<Captured> CaptureAsync(string provider, Func<TickerQDbContext, Task<int>> run)
    {
        var capture = new CaptureInterceptor();
        var builder = new DbContextOptionsBuilder<TickerQDbContext>();
        _ = provider switch
        {
            "postgres" => builder.UseNpgsql("Host=localhost;Database=x;Username=u;Password=p"),
            "sqlite" => builder.UseSqlite("Data Source=:memory:"),
            "mysql" => builder.UseMySQL("Server=localhost;Database=x;User=u;Password=p"),
            "sqlserver" => builder.UseSqlServer("Server=localhost;Database=x;User Id=u;Password=p;TrustServerCertificate=True"),
            "oracle" => builder.UseOracle("User Id=u;Password=p;Data Source=localhost/FREEPDB1"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        builder.AddInterceptors(capture, new SkipOpenInterceptor());
        await using var db = new TickerQDbContext(builder.Options);
        Assert.Equal(1, await run(db));
        return Assert.Single(capture.Commands);
    }

    private sealed class SkipOpenInterceptor : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
        public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();
        public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class CaptureInterceptor : DbCommandInterceptor
    {
        public List<Captured> Commands { get; } = [];

        private InterceptionResult<int> Capture(DbCommand command)
        {
            Commands.Add(new Captured(command.CommandText, command.Parameters.Cast<DbParameter>()
                .Select(p => $"{p.ParameterName}|{Format(p.Value)}|{p.DbType}|{p.Size}|{p.GetType().Name}").ToArray()));
            return InterceptionResult<int>.SuppressWithResult(1);
        }

        private static string Format(object value) => value switch
        {
            null => "<null>",
            DBNull => "<dbnull>",
            byte[] bytes => Convert.ToHexString(bytes),
            DateTime d => d.ToString("O"),
            _ => value.ToString(),
        };

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            => Capture(command);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Capture(command));
    }

    /// <summary>A real SQLite database shared by several contexts through one open connection.</summary>
    private sealed class SqliteStore : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private SqliteStore(SqliteConnection connection) => _connection = connection;

        public static async Task<SqliteStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var store = new SqliteStore(connection);
            await using var context = store.Context();
            await context.Database.EnsureCreatedAsync();
            return store;
        }

        public TickerQDbContext Context()
            => new(new DbContextOptionsBuilder<TickerQDbContext>().UseSqlite(_connection).Options);

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
