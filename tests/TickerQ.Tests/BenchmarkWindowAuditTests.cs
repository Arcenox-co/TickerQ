using Quartz.Benchmark.Competitors;

namespace TickerQ.Tests;

public sealed class BenchmarkWindowAuditTests
{
    private static readonly DateTimeOffset Open = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Recurring_OpeningGateCanExclude47CorrectEntries_WithoutMissingAnyNominalSlot()
    {
        var ids = Enumerable.Range(0, 100).Select(index => $"cron-{index:D3}").ToArray();
        var entries = (from second in Enumerable.Range(0, 60)
                       from index in Enumerable.Range(0, ids.Length)
                       let scheduled = Open.AddSeconds(second)
                       select Entry(ids[index], scheduled, scheduled.AddMilliseconds(5),
                           legacy: second != 0 || index >= 47)).ToArray();

        var audit = WindowAudit.AnalyzeRecurring(entries, ids, Open, Open.AddSeconds(60));

        Assert.True(audit.ExactCoverage);
        Assert.Equal(6000, audit.Expected);
        Assert.Equal(6000, audit.MatchedUnique);
        Assert.Equal(5953, audit.ObservedLegacyCount);
        Assert.Equal(47, audit.ExcludedFromLegacyCount);
        Assert.Equal(47, audit.FirstBoundaryExcluded);
        Assert.Equal(0, audit.LastBoundaryExcluded);
        Assert.Equal(0, audit.BeforeDue);
        Assert.Equal(0, audit.ArrivedAfterClose);
    }

    [Fact]
    public void OneOff_79EntriesBeforeObserver_CanAllBeOnOrAfterDue()
    {
        var ids = Enumerable.Range(0, 2000).Select(index => $"job-{index:D4}").ToArray();
        var observer = Open.AddMilliseconds(1);
        var entries = ids.Select((id, index) =>
            Entry(id, Open, index < 79 ? Open.AddTicks(index) : observer.AddTicks(index))).ToArray();

        var audit = WindowAudit.AnalyzeOneOff(entries, ids, Open, observer);

        Assert.True(audit.ExactCoverage);
        Assert.Equal(2000, audit.MatchedUnique);
        Assert.Equal(79, audit.BeforeObserver);
        Assert.Equal(0, audit.BeforeDue);
        Assert.Equal(0L, audit.MinDueDeltaTicks);
        Assert.Equal(11_999L, audit.MaxDueDeltaTicks);
        Assert.Equal(audit.MinDueDeltaTicks, audit.MinNonNegativeDueDeltaTicks);
        Assert.Equal(audit.MaxDueDeltaTicks, audit.MaxNonNegativeDueDeltaTicks);
    }

    [Fact]
    public void Recurring_LateFinalSlot_IsCoveredAndReportedAfterClose()
    {
        var close = Open.AddSeconds(2);
        var audit = WindowAudit.AnalyzeRecurring(
            [Entry("a", Open, Open), Entry("a", close.AddSeconds(-1), close.AddTicks(1), legacy: false)],
            ["a"], Open, close);

        Assert.True(audit.ExactCoverage);
        Assert.Equal(2, audit.MatchedUnique);
        Assert.Equal(1, audit.ObservedLegacyCount);
        Assert.Equal(1, audit.ArrivedAfterClose);
        Assert.Equal(1, audit.LastBoundaryExcluded);
        Assert.Equal(0, audit.FirstBoundaryExcluded);
    }

    [Fact]
    public void Recurring_EqualTotalCount_DoesNotHideAMissingSlotAndADuplicate()
    {
        var audit = WindowAudit.AnalyzeRecurring(
            [Entry("a", Open, Open), Entry("a", Open, Open.AddTicks(1))],
            ["a"], Open, Open.AddSeconds(2));

        Assert.False(audit.ExactCoverage);
        Assert.Equal(audit.Expected, audit.EntriesInWindow);
        Assert.Equal(1, audit.MatchedUnique);
        Assert.Equal(new WindowSlot("a", Open.AddSeconds(1)), Assert.Single(audit.Missing));
        Assert.Equal(new WindowDuplicate(new WindowSlot("a", Open), 2), Assert.Single(audit.Duplicates));
    }

    [Fact]
    public void Recurring_FiltersOnScheduledTime_NotActualOrLegacyGate()
    {
        var close = Open.AddSeconds(2);
        var audit = WindowAudit.AnalyzeRecurring(
            [
                Entry("a", Open.AddSeconds(-1), Open.AddTicks(1)),
                Entry("a", Open, Open.AddTicks(-1), legacy: false),
                Entry("a", Open.AddSeconds(1), close.AddTicks(1), legacy: false),
                Entry("a", close, close.AddTicks(1))
            ],
            ["a"], Open, close);

        Assert.True(audit.ExactCoverage);
        Assert.Equal(2, audit.EntriesInWindow);
        Assert.Equal(2, audit.OutOfWindowExcluded);
        Assert.Equal(0, audit.ObservedLegacyCount);
        Assert.Equal(1, audit.BeforeDue);
        Assert.Equal(1, audit.ArrivedAfterClose);
    }

    [Fact]
    public void Recurring_UnexpectedIdentityAndFractionalSlot_DoNotCountTowardCoverage()
    {
        var audit = WindowAudit.AnalyzeRecurring(
            [Entry("b", Open, Open), Entry("a", Open.AddTicks(1), Open.AddTicks(2))],
            ["a"], Open, Open.AddSeconds(1));

        Assert.False(audit.ExactCoverage);
        Assert.Equal(0, audit.MatchedUnique);
        Assert.Equal(["b"], audit.UnexpectedIdentities);
        Assert.Equal(new WindowSlot("a", Open.AddTicks(1)), Assert.Single(audit.UnexpectedSlots));
        Assert.Equal(new WindowSlot("a", Open), Assert.Single(audit.Missing));
    }

    [Fact]
    public void OneOff_ReportsMissingDuplicateUnexpectedAndWrongDueSeparately()
    {
        var audit = WindowAudit.AnalyzeOneOff(
            [
                Entry("a", Open, Open),
                Entry("a", Open, Open.AddTicks(1)),
                Entry("b", Open.AddTicks(1), Open.AddTicks(2)),
                Entry("c", Open, Open.AddTicks(3))
            ],
            ["a", "b"], Open, Open.AddTicks(4));

        Assert.False(audit.ExactCoverage);
        Assert.Equal(1, audit.MatchedUnique);
        Assert.Equal(["b"], audit.MissingIds);
        Assert.Equal(new OneOffDuplicate("a", 2), Assert.Single(audit.Duplicates));
        Assert.Equal(["c"], audit.UnexpectedIdentities);
        Assert.Equal(1, audit.MismatchedDueTimes);
    }

    [Fact]
    public void OneOff_PreservesTickPrecisionAndSeparatesTrueEarlyFromNonNegativeDeltas()
    {
        var audit = WindowAudit.AnalyzeOneOff(
            [Entry("a", Open, Open.AddTicks(-1)), Entry("b", Open, Open.AddTicks(1))],
            ["a", "b"], Open, Open);

        Assert.True(audit.ExactCoverage);
        Assert.Equal(1, audit.BeforeDue);
        Assert.Equal(-1L, audit.MinDueDeltaTicks);
        Assert.Equal(1L, audit.MaxDueDeltaTicks);
        Assert.Equal(1L, audit.MinNonNegativeDueDeltaTicks);
        Assert.Equal(1L, audit.MaxNonNegativeDueDeltaTicks);
    }

    [Fact]
    public void EmptyObservations_ReportAllMissingAndNoInventedTiming()
    {
        var recurring = WindowAudit.AnalyzeRecurring([], ["a"], Open, Open.AddSeconds(2));
        var oneOff = WindowAudit.AnalyzeOneOff([], ["a"], Open, Open);

        Assert.False(recurring.ExactCoverage);
        Assert.Equal(2, recurring.Missing.Length);
        Assert.Equal(0, recurring.EntriesInWindow);
        Assert.False(oneOff.ExactCoverage);
        Assert.Equal(["a"], oneOff.MissingIds);
        Assert.Null(oneOff.MinDueDeltaTicks);
        Assert.Null(oneOff.MaxDueDeltaTicks);
        Assert.Null(oneOff.MinNonNegativeDueDeltaTicks);
        Assert.Null(oneOff.MaxNonNegativeDueDeltaTicks);
    }

    [Fact]
    public void EquivalentUtcInstants_WithDifferentOffsets_MatchTheSameSlots()
    {
        var audit = WindowAudit.AnalyzeRecurring(
            [Entry("a", Open.ToOffset(TimeSpan.FromHours(2)), Open.AddTicks(1))],
            ["a"], Open, Open.AddSeconds(1));

        Assert.True(audit.ExactCoverage);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -10_000_000)]
    [InlineData(1, 10_000_000)]
    [InlineData(0, 10_000_001)]
    public void Recurring_RejectsInvalidWindow(long openTicks, long closeTicks)
    {
        Assert.Throws<ArgumentException>(() => WindowAudit.AnalyzeRecurring(
            [], ["a"], Open.AddTicks(openTicks), Open.AddTicks(closeTicks)));
    }

    [Fact]
    public void Analyzers_RejectInvalidExpectedIdentitiesAndObservations()
    {
        foreach (var ids in new[] { Array.Empty<string>(), new[] { "" }, new[] { " " }, new[] { "a", "a" } })
        {
            Assert.Throws<ArgumentException>(() => WindowAudit.AnalyzeRecurring([], ids, Open, Open.AddSeconds(1)));
            Assert.Throws<ArgumentException>(() => WindowAudit.AnalyzeOneOff([], ids, Open, Open));
        }
        Assert.Throws<ArgumentNullException>(() => WindowAudit.AnalyzeRecurring(null!, ["a"], Open, Open.AddSeconds(1)));
        Assert.Throws<ArgumentNullException>(() => WindowAudit.AnalyzeOneOff([], null!, Open, Open));
        Assert.Throws<ArgumentException>(() => WindowAudit.AnalyzeOneOff([], ["a"], Open, Open.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => WindowAudit.AnalyzeOneOff([null!], ["a"], Open, Open));
        Assert.Throws<ArgumentException>(() => WindowAudit.AnalyzeOneOff([Entry("", Open, Open)], ["a"], Open, Open));
    }

    private static WindowEntry Entry(string id, DateTimeOffset scheduled, DateTimeOffset actual, bool legacy = true)
        => new(id, $"{id}-{scheduled.UtcTicks}", scheduled, actual, actual.UtcTicks, legacy);
}
