namespace Quartz.Benchmark.Competitors;

/// <summary>A diagnostic job-entry observation, independent of the legacy capture gate.</summary>
internal sealed record WindowEntry(
    string ScheduleId,
    string ExecutionId,
    DateTimeOffset ScheduledFor,
    DateTimeOffset Actual,
    long Timestamp,
    bool LegacyCapturing);

internal sealed record WindowSlot(string ScheduleId, DateTimeOffset ScheduledFor);
internal sealed record WindowDuplicate(WindowSlot Slot, int Count);
internal sealed record OneOffDuplicate(string Id, int Count);

internal sealed record RecurringWindowAuditResult
{
    public int Expected { get; init; }
    public int MatchedUnique { get; init; }
    public WindowSlot[] Missing { get; init; } = [];
    public WindowDuplicate[] Duplicates { get; init; } = [];
    public string[] UnexpectedIdentities { get; init; } = [];
    public WindowSlot[] UnexpectedSlots { get; init; } = [];
    public int EntriesInWindow { get; init; }
    public int OutOfWindowExcluded { get; init; }
    public int ObservedLegacyCount { get; init; }
    public int ExcludedFromLegacyCount => EntriesInWindow - ObservedLegacyCount;
    // These count entries from the first/last nominal slot that the legacy gate excluded.
    // For a one-second window the two boundaries refer to the same slot.
    public int FirstBoundaryExcluded { get; init; }
    public int LastBoundaryExcluded { get; init; }
    public int BeforeDue { get; init; }
    public int ArrivedAfterClose { get; init; }
    public bool ExactCoverage => Missing.Length == 0 && Duplicates.Length == 0
        && UnexpectedIdentities.Length == 0 && UnexpectedSlots.Length == 0;
}

internal sealed record OneOffWindowAuditResult
{
    public int Expected { get; init; }
    public int MatchedUnique { get; init; }
    public string[] MissingIds { get; init; } = [];
    public OneOffDuplicate[] Duplicates { get; init; } = [];
    public string[] UnexpectedIdentities { get; init; } = [];
    public int MismatchedDueTimes { get; init; }
    public int EntryCount { get; init; }
    public int BeforeDue { get; init; }
    public int BeforeObserver { get; init; }
    public long? MinDueDeltaTicks { get; init; }
    public long? MaxDueDeltaTicks { get; init; }
    public long? MinNonNegativeDueDeltaTicks { get; init; }
    public long? MaxNonNegativeDueDeltaTicks { get; init; }
    public bool ExactCoverage => MissingIds.Length == 0 && Duplicates.Length == 0
        && UnexpectedIdentities.Length == 0 && MismatchedDueTimes == 0;
}

/// <summary>
/// Pure analysis of diagnostic observations. Coverage uses nominal schedule identities and
/// timestamps, not an observer's arrival at the measurement boundary. No waiting or engine
/// behavior is changed here; timestamp values and execution IDs remain in the input evidence.
/// </summary>
internal static class WindowAudit
{
    public static RecurringWindowAuditResult AnalyzeRecurring(
        WindowEntry[] entries, string[] expectedScheduleIds, DateTimeOffset open, DateTimeOffset close)
    {
        ValidateEntries(entries);
        var identities = ValidateIdentities(expectedScheduleIds);
        if (close <= open || open.UtcTicks % TimeSpan.TicksPerSecond != 0
            || close.UtcTicks % TimeSpan.TicksPerSecond != 0)
            throw new ArgumentException("The window must have increasing, whole-second UTC boundaries.");

        var seconds = checked((int)((close.UtcTicks - open.UtcTicks) / TimeSpan.TicksPerSecond));
        var expectedCount = checked(seconds * identities.Count);
        var expected = new HashSet<WindowSlot>();
        foreach (var id in identities)
        {
            for (var second = 0; second < seconds; second++)
                expected.Add(new WindowSlot(id, open.AddSeconds(second)));
        }

        var inWindow = entries.Where(entry => entry.ScheduledFor >= open && entry.ScheduledFor < close)
            .ToArray();
        var groups = inWindow.GroupBy(entry => new WindowSlot(entry.ScheduleId, entry.ScheduledFor))
            .ToDictionary(group => group.Key, group => group.Count());
        return new RecurringWindowAuditResult
        {
            Expected = expectedCount,
            MatchedUnique = groups.Keys.Count(expected.Contains),
            Missing = OrderSlots(expected.Where(slot => !groups.ContainsKey(slot))),
            Duplicates = groups.Where(group => expected.Contains(group.Key) && group.Value > 1)
                .OrderBy(group => group.Key.ScheduledFor)
                .ThenBy(group => group.Key.ScheduleId, StringComparer.Ordinal)
                .Select(group => new WindowDuplicate(group.Key, group.Value)).ToArray(),
            UnexpectedIdentities = inWindow.Select(entry => entry.ScheduleId)
                .Where(id => !identities.Contains(id)).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            UnexpectedSlots = OrderSlots(groups.Keys.Where(slot =>
                identities.Contains(slot.ScheduleId) && !expected.Contains(slot))),
            EntriesInWindow = inWindow.Length,
            OutOfWindowExcluded = entries.Length - inWindow.Length,
            ObservedLegacyCount = inWindow.Count(entry => entry.LegacyCapturing),
            FirstBoundaryExcluded = inWindow.Count(entry => !entry.LegacyCapturing && entry.ScheduledFor == open),
            LastBoundaryExcluded = inWindow.Count(entry => !entry.LegacyCapturing && entry.ScheduledFor == close.AddSeconds(-1)),
            BeforeDue = inWindow.Count(entry => entry.Actual < entry.ScheduledFor),
            ArrivedAfterClose = inWindow.Count(entry => entry.Actual >= close)
        };
    }

    public static OneOffWindowAuditResult AnalyzeOneOff(
        WindowEntry[] entries, string[] expectedIds, DateTimeOffset due, DateTimeOffset observerOpen)
    {
        ValidateEntries(entries);
        var identities = ValidateIdentities(expectedIds);
        if (observerOpen < due)
            throw new ArgumentException("The observer opening must be at or after the due instant.", nameof(observerOpen));

        var groups = entries.Where(entry => identities.Contains(entry.ScheduleId) && entry.ScheduledFor == due)
            .GroupBy(entry => entry.ScheduleId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var deltas = entries.Select(entry => entry.Actual.UtcTicks - due.UtcTicks).ToArray();
        var nonNegative = deltas.Where(delta => delta >= 0).ToArray();
        return new OneOffWindowAuditResult
        {
            Expected = identities.Count,
            MatchedUnique = groups.Count,
            MissingIds = identities.Where(id => !groups.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Duplicates = groups.Where(group => group.Value > 1)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new OneOffDuplicate(group.Key, group.Value)).ToArray(),
            UnexpectedIdentities = entries.Select(entry => entry.ScheduleId)
                .Where(id => !identities.Contains(id)).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            MismatchedDueTimes = entries.Count(entry => entry.ScheduledFor != due),
            EntryCount = entries.Length,
            BeforeDue = entries.Count(entry => entry.Actual < due),
            BeforeObserver = entries.Count(entry => entry.Actual < observerOpen),
            MinDueDeltaTicks = deltas.Length == 0 ? null : deltas.Min(),
            MaxDueDeltaTicks = deltas.Length == 0 ? null : deltas.Max(),
            MinNonNegativeDueDeltaTicks = nonNegative.Length == 0 ? null : nonNegative.Min(),
            MaxNonNegativeDueDeltaTicks = nonNegative.Length == 0 ? null : nonNegative.Max()
        };
    }

    private static WindowSlot[] OrderSlots(IEnumerable<WindowSlot> slots)
        => slots.OrderBy(slot => slot.ScheduledFor)
            .ThenBy(slot => slot.ScheduleId, StringComparer.Ordinal).ToArray();

    private static HashSet<string> ValidateIdentities(string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Expected identities must be nonempty strings.", nameof(ids));
        var result = new HashSet<string>(ids, StringComparer.Ordinal);
        if (result.Count != ids.Length)
            throw new ArgumentException("Expected identities must be unique.", nameof(ids));
        return result;
    }

    private static void ValidateEntries(WindowEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.ScheduleId)
            || string.IsNullOrWhiteSpace(entry.ExecutionId)))
            throw new ArgumentException("Entries require schedule and execution identities.", nameof(entries));
    }
}
