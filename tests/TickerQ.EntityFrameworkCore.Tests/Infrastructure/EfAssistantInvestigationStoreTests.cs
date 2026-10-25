using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfAssistantInvestigationStoreTests
{
    [Fact]
    public async Task Store_deduplicates_per_namespace_and_supports_review_and_recovery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db,
            new AssistantHistoryOptions { MaxInvestigationsPerNamespace = 10 });
        var executionId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

        var id = await store.TryStartAsync(Seed("orders", executionId, occurredAt));
        Assert.NotNull(id);
        Assert.Null(await store.TryStartAsync(Seed("orders", executionId, occurredAt)));
        Assert.NotNull(await store.TryStartAsync(Seed("billing", executionId, occurredAt)));

        await store.CompleteAsync("orders", id!.Value, "The charge endpoint rejected the card.", "ops-model");
        Assert.True(await store.SetReviewStateAsync("orders", id.Value,
            AssistantInvestigationValues.Reviewed, "operator-1"));
        var successfulExecutionId = Guid.NewGuid();
        Assert.Equal(1, await store.MarkRecoveredAsync("orders", "Invoices.Charge",
            successfulExecutionId, occurredAt.AddMinutes(5)));

        var page = await store.ListAsync("orders", new AssistantInvestigationQuery
        {
            Search = "Invoices",
            ReviewState = AssistantInvestigationValues.Reviewed,
        });
        var saved = Assert.Single(page.Items);
        Assert.Equal(id, saved.Id);
        Assert.Equal(AssistantInvestigationValues.Completed, saved.State);
        Assert.Equal(successfulExecutionId, saved.RecoveredByExecutionId);
        Assert.Equal("ops-model", saved.Model);

        var overview = await store.GetOverviewAsync("orders");
        Assert.Equal(1, overview.Total);
        Assert.Equal(1, overview.Recovered);
        Assert.Equal(0, overview.AwaitingReview);
        Assert.Null(await store.GetAsync("another-namespace", id.Value));
    }

    [Fact]
    public async Task Store_groups_repeated_failures_without_crossing_namespaces()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var now = DateTime.UtcNow;

        foreach (var executionId in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            var id = await store.TryStartAsync(Seed("orders", executionId, now));
            await store.CompleteAsync("orders", id!.Value, "summary", "ops-model");
        }
        var other = await store.TryStartAsync(Seed("billing", Guid.NewGuid(), now));
        await store.CompleteAsync("billing", other!.Value, "summary", "ops-model");

        var overview = await store.GetOverviewAsync("orders");
        var pattern = Assert.Single(overview.Patterns);
        Assert.Equal("Invoices.Charge", pattern.FunctionName);
        Assert.Equal(2, pattern.Count);
    }

    [Fact]
    public async Task Grouped_list_pages_function_cards_without_splitting_their_history()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db,
            new AssistantHistoryOptions { MaxInvestigationsPerNamespace = 50 });
        var now = DateTime.UtcNow;

        Assert.NotNull(await store.TryStartAsync(Seed("orders", Guid.NewGuid(),
            now.AddMinutes(-1), "Demo.AnotherFailure")));
        for (var index = 0; index < 20; index++)
            Assert.NotNull(await store.TryStartAsync(Seed("orders", Guid.NewGuid(),
                now.AddSeconds(index), "Demo.Failure")));

        var firstPage = await store.ListAsync("orders", new AssistantInvestigationQuery
        {
            GroupByFunction = true,
            PageNumber = 1,
            PageSize = 1,
        });
        var secondPage = await store.ListAsync("orders", new AssistantInvestigationQuery
        {
            GroupByFunction = true,
            PageNumber = 2,
            PageSize = 1,
        });

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(20, firstPage.Items.Count);
        Assert.All(firstPage.Items, item => Assert.Equal("Demo.Failure", item.FunctionName));
        Assert.Single(secondPage.Items);
        Assert.Equal("Demo.AnotherFailure", secondPage.Items[0].FunctionName);
    }

    [Fact]
    public async Task Reusable_diagnosis_requires_matching_namespace_fingerprint_and_completed_state()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var fingerprint = new string('A', 64);
        var originalExecutionId = Guid.NewGuid();
        var originalId = await store.TryStartAsync(Seed("orders", originalExecutionId,
            DateTime.UtcNow.AddMinutes(-1), fingerprint: fingerprint));
        await store.CompleteAsync("orders", originalId!.Value, "Review the charge code.", "ops-model");

        var pendingId = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow,
            fingerprint: new string('B', 64)));
        Assert.NotNull(pendingId);

        var reusable = await store.FindReusableAsync("orders", fingerprint, Guid.NewGuid());

        Assert.NotNull(reusable);
        Assert.Equal(originalId, reusable.Id);
        Assert.Equal("Review the charge code.", reusable.Summary);
        Assert.Null(await store.FindReusableAsync("billing", fingerprint, Guid.NewGuid()));
        Assert.Null(await store.FindReusableAsync("orders", new string('B', 64), Guid.NewGuid()));
        Assert.Null(await store.FindReusableAsync("orders", fingerprint, originalExecutionId));
    }

    [Fact]
    public async Task Reading_an_older_investigation_does_not_move_it_ahead_of_newer_items()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());

        var olderId = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-2)));
        var newerId = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1)));
        var now = DateTime.UtcNow;
        var rows = await db.Set<AssistantInvestigationEntity>().ToDictionaryAsync(x => x.Id);
        rows[olderId!.Value].CreatedAt = now.AddMinutes(-2);
        rows[olderId.Value].UpdatedAt = now.AddMinutes(-2);
        rows[newerId!.Value].CreatedAt = now.AddMinutes(-1);
        rows[newerId.Value].UpdatedAt = now.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.True(await store.SetReviewStateAsync("orders", olderId.Value,
            AssistantInvestigationValues.Reviewed, "operator-1"));

        var page = await store.ListAsync("orders", new AssistantInvestigationQuery());
        var overview = await store.GetOverviewAsync("orders");
        Assert.Equal(newerId.Value, page.Items[0].Id);
        Assert.Equal(olderId.Value, page.Items[1].Id);
        Assert.Equal(newerId.Value, overview.Recent[0].Id);
        Assert.Equal(olderId.Value, overview.Recent[1].Id);
    }

    [Fact]
    public async Task Reading_an_investigation_does_not_extend_its_time_based_retention()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db,
            new AssistantHistoryOptions { InvestigationRetention = TimeSpan.FromDays(1) });

        var oldId = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow.AddDays(-2)));
        await store.CompleteAsync("orders", oldId!.Value, "old diagnosis", "ops-model");
        await db.Set<AssistantInvestigationEntity>().Where(x => x.Id == oldId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedAt,
                ClientValues.UtcNow.AddDays(-2)));
        Assert.True(await store.SetReviewStateAsync("orders", oldId.Value,
            AssistantInvestigationValues.Reviewed, "operator-1"));

        Assert.NotNull(await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow)));

        Assert.Null(await store.GetAsync("orders", oldId.Value));
    }

    [Theory]
    [InlineData(AssistantInvestigationValues.Reviewed)]
    [InlineData(AssistantInvestigationValues.Resolved)]
    public async Task Optional_review_state_retention_starts_at_the_state_transition(string reviewState)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db,
            new AssistantHistoryOptions
            {
                InvestigationRetention = TimeSpan.FromDays(30),
                ReadInvestigationRetention = TimeSpan.FromDays(1),
                ResolvedInvestigationRetention = TimeSpan.FromDays(1),
            });

        var expiringId = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow));
        await store.CompleteAsync("orders", expiringId!.Value, "diagnosis", "ops-model");
        Assert.True(await store.SetReviewStateAsync("orders", expiringId.Value,
            reviewState, "operator-1"));
        await db.Set<AssistantInvestigationEntity>().Where(x => x.Id == expiringId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReviewedAt,
                ClientValues.UtcNow.AddDays(-2)));

        Assert.NotNull(await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow)));

        Assert.Null(await store.GetAsync("orders", expiringId.Value));
    }

    [Theory]
    [InlineData(AssistantInvestigationValues.Reviewed)]
    [InlineData(AssistantInvestigationValues.Resolved)]
    public async Task Null_review_state_retention_keeps_the_general_window(string reviewState)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db,
            new AssistantHistoryOptions { InvestigationRetention = TimeSpan.FromDays(30) });

        var retainedId = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow));
        await store.CompleteAsync("orders", retainedId!.Value, "diagnosis", "ops-model");
        Assert.True(await store.SetReviewStateAsync("orders", retainedId.Value,
            reviewState, "operator-1"));
        await db.Set<AssistantInvestigationEntity>().Where(x => x.Id == retainedId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReviewedAt,
                ClientValues.UtcNow.AddDays(-2)));

        Assert.NotNull(await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow)));

        Assert.NotNull(await store.GetAsync("orders", retainedId.Value));
    }

    [Theory]
    [InlineData(AssistantInvestigationValues.Reviewed)]
    [InlineData(AssistantInvestigationValues.Resolved)]
    public async Task Configured_review_state_retention_replaces_the_general_window(string reviewState)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db,
            new AssistantHistoryOptions
            {
                InvestigationRetention = TimeSpan.FromDays(1),
                ReadInvestigationRetention = TimeSpan.FromDays(7),
                ResolvedInvestigationRetention = TimeSpan.FromDays(7),
            });

        var retainedId = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow));
        await store.CompleteAsync("orders", retainedId!.Value, "diagnosis", "ops-model");
        Assert.True(await store.SetReviewStateAsync("orders", retainedId.Value,
            reviewState, "operator-1"));
        await db.Set<AssistantInvestigationEntity>().Where(x => x.Id == retainedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.CreatedAt, ClientValues.UtcNow.AddDays(-2))
                .SetProperty(x => x.ReviewedAt, ClientValues.UtcNow));

        Assert.NotNull(await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow)));

        Assert.NotNull(await store.GetAsync("orders", retainedId.Value));
    }

    [Fact]
    public async Task Delete_operations_are_scoped_to_namespace_and_function()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var now = DateTime.UtcNow;

        var firstExecutionId = Guid.NewGuid();
        var secondExecutionId = Guid.NewGuid();
        var first = await store.TryStartAsync(Seed("orders", firstExecutionId, now, "Invoices.Charge"));
        var second = await store.TryStartAsync(Seed("orders", secondExecutionId, now, "Invoices.Charge"));
        var otherFunction = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), now, "Invoices.Send"));
        var otherNamespace = await store.TryStartAsync(Seed("billing", Guid.NewGuid(), now, "Invoices.Charge"));

        Assert.False(await store.DeleteAsync("billing", first!.Value));
        Assert.True(await store.DeleteAsync("orders", first.Value));
        await store.CompleteAsync("orders", first.Value, "late result", "ops-model");
        Assert.Equal(1, await store.DeleteByFunctionAsync("orders", "Invoices.Charge"));
        Assert.Null(await store.GetAsync("orders", first.Value));
        Assert.Null(await store.GetAsync("orders", second!.Value));
        Assert.NotNull(await store.GetAsync("orders", otherFunction!.Value));
        Assert.NotNull(await store.GetAsync("billing", otherNamespace!.Value));

        // A deleted diagnosis stays hidden, but its scrubbed execution tombstone prevents the
        // automatic scanner from buying another diagnosis for the same historical failure.
        Assert.Null(await store.TryStartAsync(Seed("orders", firstExecutionId, now, "Invoices.Charge")));
        Assert.Null(await store.TryStartAsync(Seed("orders", secondExecutionId, now, "Invoices.Charge")));
        var visible = await store.ListAsync("orders", new AssistantInvestigationQuery());
        Assert.Single(visible.Items);
        Assert.Equal(otherFunction, visible.Items[0].Id);

        var tombstones = await db.Set<AssistantInvestigationEntity>().AsNoTracking()
            .Where(x => x.ApplicationNamespace == "orders" &&
                x.State == AssistantInvestigationValues.Dismissed)
            .ToListAsync();
        Assert.Equal(2, tombstones.Count);
        Assert.All(tombstones, row =>
        {
            Assert.Empty(row.Summary);
            Assert.Empty(row.SourcePath);
            // Required columns hold a placeholder because Oracle cannot store "" in them.
            Assert.Equal(EfAssistantInvestigationStore<InvestigationContext>.MissingText, row.FunctionName);
            Assert.Equal(EfAssistantInvestigationStore<InvestigationContext>.MissingText, row.EvidenceFingerprint);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Completed_investigation_without_a_summary_is_not_reused(string? summary)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var fingerprint = new string('C', 64);
        var id = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow, fingerprint: fingerprint));
        await store.CompleteAsync("orders", id!.Value, summary!, "ops-model");

        // Simulate how Oracle reads an empty string back.
        if (summary == "")
            await db.Set<AssistantInvestigationEntity>().Where(x => x.Id == id.Value)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Summary, (string?)null));

        Assert.Null(await store.FindReusableAsync("orders", fingerprint, Guid.NewGuid()));
    }

    [Fact]
    public async Task Missing_required_text_is_stored_as_a_placeholder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var seed = Seed("orders", Guid.NewGuid(), DateTime.UtcNow);
        seed.FunctionName = null!;
        seed.ExecutionType = "";
        seed.ExecutionStatus = null!;
        seed.Classification = "";
        seed.Trigger = null!;

        var id = await store.TryStartAsync(seed);

        var saved = await store.GetAsync("orders", id!.Value);
        Assert.NotNull(saved);
        Assert.Equal(EfAssistantInvestigationStore<InvestigationContext>.MissingText, saved!.FunctionName);
        Assert.Equal(EfAssistantInvestigationStore<InvestigationContext>.MissingText, saved.ExecutionType);
        Assert.Equal(EfAssistantInvestigationStore<InvestigationContext>.MissingText, saved.ExecutionStatus);
        Assert.Equal("Unclassified", saved.Classification);
        Assert.Equal(AssistantInvestigationValues.User, saved.Trigger);
    }

    [Fact]
    public async Task Optional_text_stored_as_null_is_returned_as_empty()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var id = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow));

        // Oracle stores "" as NULL; the store must return "" on every database.
        await db.Set<AssistantInvestigationEntity>().Where(x => x.Id == id!.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Summary, (string?)null)
                .SetProperty(x => x.Model, (string?)null)
                .SetProperty(x => x.SourceRepository, (string?)null)
                .SetProperty(x => x.SourceRevision, (string?)null)
                .SetProperty(x => x.SourcePath, (string?)null)
                .SetProperty(x => x.StartedByUserId, (string?)null)
                .SetProperty(x => x.ReviewedByUserId, (string?)null)
                .SetProperty(x => x.FailureMessage, (string?)null));

        var saved = await store.GetAsync("orders", id!.Value);
        var listed = Assert.Single((await store.ListAsync("orders", new AssistantInvestigationQuery())).Items);
        foreach (var record in new[] { saved!, listed })
        {
            Assert.Equal("", record.Summary);
            Assert.Equal("", record.Model);
            Assert.Equal("", record.SourceRepository);
            Assert.Equal("", record.SourceRevision);
            Assert.Equal("", record.SourcePath);
            Assert.Equal("", record.StartedByUserId);
            Assert.Equal("", record.ReviewedByUserId);
            Assert.Equal("", record.FailureMessage);
        }
    }

    [Fact]
    public async Task Evidence_lease_is_exclusive_owner_checked_and_recoverable_after_expiry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var fingerprint = new string('A', 64);
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();

        Assert.True(await store.TryAcquireEvidenceLeaseAsync("orders", fingerprint,
            firstOwner, DateTime.UtcNow.AddMinutes(5)));
        Assert.False(await store.TryAcquireEvidenceLeaseAsync("orders", fingerprint,
            secondOwner, DateTime.UtcNow.AddMinutes(5)));
        Assert.False(await store.RenewEvidenceLeaseAsync("orders", fingerprint,
            secondOwner, DateTime.UtcNow.AddMinutes(5)));
        await store.ReleaseEvidenceLeaseAsync("orders", fingerprint, secondOwner);
        Assert.False(await store.TryAcquireEvidenceLeaseAsync("orders", fingerprint,
            secondOwner, DateTime.UtcNow.AddMinutes(5)));

        await db.Set<AssistantInvestigationLeaseEntity>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseUntil,
                ClientValues.UtcNow.AddSeconds(-1)));
        Assert.True(await store.TryAcquireEvidenceLeaseAsync("orders", fingerprint,
            secondOwner, DateTime.UtcNow.AddMinutes(5)));
        await store.ReleaseEvidenceLeaseAsync("orders", fingerprint, secondOwner);
        Assert.True(await store.TryAcquireEvidenceLeaseAsync("orders", fingerprint,
            firstOwner, DateTime.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task Model_call_budget_is_rolling_and_scoped_to_namespace()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var now = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(await store.TryReserveModelCallAsync("orders", now, 2));
        Assert.True(await store.TryReserveModelCallAsync("orders", now.AddMinutes(1), 2));
        Assert.False(await store.TryReserveModelCallAsync("orders", now.AddMinutes(2), 2));
        Assert.True(await store.TryReserveModelCallAsync("billing", now.AddMinutes(2), 2));
        Assert.True(await store.TryReserveModelCallAsync("orders", now.AddMinutes(61), 2));

        var orders = await db.Set<AssistantInvestigationBudgetEntity>().AsNoTracking()
            .SingleAsync(x => x.ApplicationNamespace == "orders");
        Assert.Equal(3, orders.Version);
        Assert.Single(orders.StartedAtTicks.Split(','));
    }

    [Fact]
    public async Task Abandon_removes_only_the_owned_namespace_processing_record()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var id = await store.TryStartAsync(Seed("orders", Guid.NewGuid(), DateTime.UtcNow));

        await store.AbandonProcessingAsync("billing", id!.Value);
        Assert.NotNull(await store.GetAsync("orders", id.Value));
        await store.AbandonProcessingAsync("orders", id.Value);
        Assert.Null(await store.GetAsync("orders", id.Value));
    }

    [Fact]
    public async Task Stale_processing_claim_can_be_recovered_without_deleting_a_recent_claim()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InvestigationContext>().UseSqlite(connection).Options;
        await using var db = new InvestigationContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantInvestigationStore<InvestigationContext>(db, new AssistantHistoryOptions());
        var staleExecution = Guid.NewGuid();
        var recentExecution = Guid.NewGuid();
        var staleId = await store.TryStartAsync(Seed("orders", staleExecution, DateTime.UtcNow));
        var recentId = await store.TryStartAsync(Seed("orders", recentExecution, DateTime.UtcNow));
        await db.Set<AssistantInvestigationEntity>().Where(x => x.Id == staleId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt,
                ClientValues.UtcNow.AddMinutes(-10)));

        await store.AbandonStaleProcessingAsync("orders", staleExecution,
            DateTime.UtcNow.AddMinutes(-5));
        await store.AbandonStaleProcessingAsync("orders", recentExecution,
            DateTime.UtcNow.AddMinutes(-5));

        Assert.Null(await store.GetAsync("orders", staleId!.Value));
        Assert.NotNull(await store.GetAsync("orders", recentId!.Value));
        Assert.NotNull(await store.TryStartAsync(Seed("orders", staleExecution, DateTime.UtcNow)));
    }

    private static AssistantInvestigationRecord Seed(string applicationNamespace, Guid executionId,
        DateTime occurredAt, string functionName = "Invoices.Charge", string? fingerprint = null) => new()
    {
        ApplicationNamespace = applicationNamespace,
        ExecutionId = executionId,
        ExecutionType = "TimeTicker",
        FunctionName = functionName,
        ExecutionStatus = "Failed",
        Classification = "Application error",
        Trigger = AssistantInvestigationValues.Automatic,
        EvidenceFingerprint = fingerprint ?? Guid.NewGuid().ToString("N"),
        OccurredAt = occurredAt,
    };

    private sealed class InvestigationContext(DbContextOptions<InvestigationContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AssistantInvestigationConfigurations());
            modelBuilder.ApplyConfiguration(new AssistantInvestigationLeaseConfigurations());
            modelBuilder.ApplyConfiguration(new AssistantInvestigationBudgetConfigurations());
        }
    }
}
