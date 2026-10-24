using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.Conformance;

/// <summary>
/// Runs the assistant investigation store against each relational target. Oracle stores an empty
/// string as NULL, so these steps cover empty and missing text in both required and optional columns.
/// </summary>
[Collection("Relational conformance")]
public sealed class RelationalAssistantConformanceTests
{
    private const string Missing = EfAssistantInvestigationStore<TickerQDbContext>.MissingText;

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Investigation_store_handles_empty_and_missing_text(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target, createTables: false);
        var options = Options(database);
        await using (var schema = new TickerQDbContext(options))
            await schema.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await using var db = new TickerQDbContext(options);
        var store = new EfAssistantInvestigationStore<TickerQDbContext>(db,
            new AssistantHistoryOptions { MaxInvestigationsPerNamespace = 50 });
        var now = DateTime.UtcNow;

        // A missing function name and execution type are stored as the placeholder.
        var incomplete = Seed(Guid.NewGuid(), now, "Invoices.Charge");
        incomplete.FunctionName = null!;
        incomplete.ExecutionType = "";
        var incompleteId = await store.TryStartAsync(incomplete);
        Assert.NotNull(incompleteId);
        var stored = await store.GetAsync("orders", incompleteId!.Value);
        Assert.Equal(Missing, stored!.FunctionName);
        Assert.Equal(Missing, stored.ExecutionType);

        // Optional text comes back as "" even where the database stored NULL.
        Assert.Equal("", stored.Summary);
        Assert.Equal("", stored.SourcePath);
        Assert.Equal("", stored.ReviewedByUserId);
        Assert.Equal("", stored.FailureMessage);

        // A completion without a summary is never reused, while one with a summary is.
        var emptyFingerprint = new string('E', 64);
        var emptyId = await store.TryStartAsync(Seed(Guid.NewGuid(), now, "Invoices.Charge", emptyFingerprint));
        await store.CompleteAsync("orders", emptyId!.Value, "", "ops-model");
        Assert.Null(await store.FindReusableAsync("orders", emptyFingerprint, Guid.NewGuid()));

        var fingerprint = new string('F', 64);
        var firstExecution = Guid.NewGuid();
        var first = await store.TryStartAsync(Seed(firstExecution, now, "Invoices.Charge", fingerprint));
        await store.CompleteAsync("orders", first!.Value, "Card was declined.", "ops-model");
        var reused = await store.FindReusableAsync("orders", fingerprint, Guid.NewGuid());
        Assert.Equal(first, reused?.Id);
        Assert.Equal("Card was declined.", reused!.Summary);

        var failed = await store.TryStartAsync(Seed(Guid.NewGuid(), now, "Invoices.Send"));
        await store.FailAsync("orders", failed!.Value, "");
        Assert.Equal("", (await store.GetAsync("orders", failed.Value))!.FailureMessage);

        Assert.True(await store.SetReviewStateAsync("orders", first.Value,
            AssistantInvestigationValues.Reviewed, "operator-1"));
        // Both completed "Invoices.Charge" investigations are marked recovered.
        Assert.Equal(2, await store.MarkRecoveredAsync("orders", "Invoices.Charge", Guid.NewGuid(), now.AddMinutes(1)));
        var grouped = await store.ListAsync("orders", new AssistantInvestigationQuery { GroupByFunction = true });
        Assert.All(grouped.Items, item => Assert.NotNull(item.Summary));
        Assert.True((await store.GetOverviewAsync("orders")).Total >= 4);

        // Deleting writes a tombstone. Its required columns hold the placeholder, it is hidden from
        // every read, and it still blocks a second diagnosis of the same execution.
        Assert.True(await store.DeleteAsync("orders", first.Value));
        Assert.Equal(1, await store.DeleteByFunctionAsync("orders", "Invoices.Send"));
        Assert.Null(await store.GetAsync("orders", first.Value));
        Assert.Null(await store.GetAsync("orders", failed.Value));
        Assert.Null(await store.TryStartAsync(Seed(firstExecution, now, "Invoices.Charge")));
        var visible = await store.ListAsync("orders", new AssistantInvestigationQuery());
        Assert.DoesNotContain(visible.Items, item => item.Id == first.Value || item.Id == failed.Value);

        db.ChangeTracker.Clear();
        var tombstones = await db.Set<AssistantInvestigationEntity>().AsNoTracking()
            .Where(x => x.State == AssistantInvestigationValues.Dismissed)
            .ToListAsync();
        Assert.Equal(2, tombstones.Count);
        Assert.All(tombstones, row =>
        {
            Assert.Equal(Missing, row.FunctionName);
            Assert.Equal(Missing, row.EvidenceFingerprint);
            Assert.True(string.IsNullOrEmpty(row.Summary));
        });
    }

    private static DbContextOptions<TickerQDbContext> Options(ConformanceDatabase database)
    {
        var builder = RelationalConformanceTargets.Configure(
            new DbContextOptionsBuilder<TickerQDbContext>(), database.Target, database.ConnectionString);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new ConformanceSchemaExtension(database.Schema));
        builder.ReplaceService<IModelCustomizer, ConformanceSchemaModelCustomizer>();
        builder.AddInterceptors(MySqlDeleteAliasInterceptor.Instance);
        return builder.Options;
    }

    private static AssistantInvestigationRecord Seed(Guid executionId, DateTime occurredAt, string functionName,
        string? fingerprint = null) => new()
    {
        ApplicationNamespace = "orders",
        ExecutionId = executionId,
        ExecutionType = "TimeTicker",
        FunctionName = functionName,
        ExecutionStatus = "Failed",
        Classification = "Application error",
        Trigger = AssistantInvestigationValues.Automatic,
        EvidenceFingerprint = fingerprint ?? Guid.NewGuid().ToString("N"),
        OccurredAt = occurredAt,
    };
}
