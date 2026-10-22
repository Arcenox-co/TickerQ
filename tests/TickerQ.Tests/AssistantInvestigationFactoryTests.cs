using NSubstitute;
using TickerQ.Dashboard.Assistant;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class AssistantInvestigationFactoryTests
{
    [Theory]
    [InlineData("Investigate execution 117ae488-02ac-47f5-a17a-24729f7deea6.", true)]
    [InlineData("Investigate execution 117ae488-02ac-47f5-a17a-24729f7deea6 and explain it", true)]
    [InlineData("Please inspect 117ae488-02ac-47f5-a17a-24729f7deea6", false)]
    [InlineData("Investigate execution not-a-guid", false)]
    public void Execution_prompt_is_deliberate_and_unambiguous(string prompt, bool expected)
    {
        Assert.Equal(expected, AssistantInvestigationFactory.TryReadExecutionId(prompt, out _));
    }

    [Fact]
    public async Task CreateAsync_builds_safe_structured_evidence_for_a_time_execution()
    {
        var id = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 10, 22, 9, 30, 0, DateTimeKind.Utc);
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetTimeTickerByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto
        {
            Id = id,
            ParentId = parentId,
            FunctionName = "Invoices.Charge",
            Status = TickerStatus.Failed,
            ExecutedAt = occurredAt,
            ExceptionMessage = """
                {"Type":"System.InvalidOperationException","Message":"Card declined","SourceFile":"/private/build/Jobs/ChargeInvoice.cs","SourceLine":42}
                """,
        });

        var result = await AssistantInvestigationFactory.CreateAsync(data, id, "orders-production",
            AssistantInvestigationValues.User, "operator-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("orders-production", result.ApplicationNamespace);
        Assert.Equal(parentId, result.ChainRootId);
        Assert.Equal("Application error", result.Classification);
        Assert.Equal("ChargeInvoice.cs", result.SourcePath);
        Assert.Equal(42, result.SourceLine);
        Assert.Equal(64, result.EvidenceFingerprint.Length);
        Assert.Equal("operator-1", result.StartedByUserId);
        Assert.Equal(occurredAt, result.OccurredAt);
    }

    [Fact]
    public async Task Fingerprint_reuses_the_same_application_frame_despite_dynamic_message_values()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var otherLineId = Guid.NewGuid();
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetTimeTickerByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.ArgAt<Guid>(0);
            var line = id == otherLineId ? 43 : 42;
            var message = id == firstId ? "Order 1001 failed" : "Order 9876 failed";
            return new TimeTickerFlatDto
            {
                Id = id,
                FunctionName = "Invoices.Charge",
                Status = TickerStatus.Failed,
                ExecutedAt = DateTime.UtcNow,
                ExceptionMessage = $$"""
                    {"Type":"System.InvalidOperationException","Message":"{{message}}","SourceFile":"/private/build/Jobs/ChargeInvoice.cs","SourceLine":{{line}}}
                    """,
            };
        });

        var first = await AssistantInvestigationFactory.CreateAsync(data, firstId, "orders",
            AssistantInvestigationValues.Automatic, null, CancellationToken.None);
        var second = await AssistantInvestigationFactory.CreateAsync(data, secondId, "orders",
            AssistantInvestigationValues.Automatic, null, CancellationToken.None);
        var otherLine = await AssistantInvestigationFactory.CreateAsync(data, otherLineId, "orders",
            AssistantInvestigationValues.Automatic, null, CancellationToken.None);

        Assert.Equal(first!.EvidenceFingerprint, second!.EvidenceFingerprint);
        Assert.NotEqual(first.EvidenceFingerprint, otherLine!.EvidenceFingerprint);
    }

    [Fact]
    public async Task CreateAsync_treats_unspecified_persisted_execution_times_as_utc()
    {
        var id = Guid.NewGuid();
        var persistedUtc = new DateTime(2026, 10, 22, 13, 7, 0, DateTimeKind.Unspecified);
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetTimeTickerByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto
        {
            Id = id,
            FunctionName = "Invoices.Charge",
            Status = TickerStatus.Failed,
            ExecutedAt = persistedUtc,
        });

        var result = await AssistantInvestigationFactory.CreateAsync(data, id, "orders-production",
            AssistantInvestigationValues.Automatic, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result.OccurredAt.Kind);
        Assert.Equal(13, result.OccurredAt.Hour);
        Assert.Equal(DateTime.SpecifyKind(persistedUtc, DateTimeKind.Utc), result.OccurredAt);
    }

    [Fact]
    public async Task CreateAsync_falls_back_to_a_cron_occurrence_and_classifies_cancellation()
    {
        var id = Guid.NewGuid();
        var scheduledFor = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetTimeTickerByIdAsync(id, Arg.Any<CancellationToken>()).Returns((TimeTickerFlatDto)null!);
        data.GetCronOccurrenceByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new CronOccurrenceFlatDto
        {
            Id = id,
            FunctionName = "Invoices.Export",
            Status = TickerStatus.Cancelled,
            ScheduledFor = scheduledFor,
            ExceptionMessage = "A task was canceled.",
        });

        var result = await AssistantInvestigationFactory.CreateAsync(data, id, "orders-production",
            AssistantInvestigationValues.Automatic, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("CronOccurrence", result.ExecutionType);
        Assert.Equal("Cancellation", result.Classification);
        Assert.Equal(scheduledFor, result.OccurredAt);
    }
}
