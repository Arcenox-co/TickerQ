using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Promises
{
    /// <summary>How a row's work was missed. A failed job is not one: executions, retries and alerts own failures.</summary>
    internal enum TickerPromiseFindingKind
    {
        /// <summary>Never scheduled: no job was found for the row.</summary>
        NoJob,
        /// <summary>The row's job never ran: it was cancelled or skipped, or is still waiting long after it was due.</summary>
        NeverRan,
        /// <summary>The row's job succeeded, but the row in the application's data still doesn't meet DoneWhen. Reported only.</summary>
        DoneButNotApplied,
        /// <summary>The row's job succeeded more than once. Reported only: the work already happened twice.</summary>
        RanTwice,
        /// <summary>An extra job is waiting for a row that already has a job that succeeded, is running or is waiting.</summary>
        WillRunTwice,
    }

    /// <summary>What "Run fix" does for a finding.</summary>
    internal enum TickerPromiseFix
    {
        None,
        /// <summary>Schedules a new job with the row's request.</summary>
        NewJob,
        /// <summary>Runs the row's waiting job now.</summary>
        RunNow,
        /// <summary>Cancels the row's extra waiting jobs.</summary>
        CancelExtra,
    }

    internal sealed class TickerPromiseFinding
    {
        public Guid Id { get; set; }
        public TickerPromiseFindingKind Kind { get; set; }
        /// <summary>The row's matchBy values, for example "acme · 1042".</summary>
        public string Key { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public TickerPromiseLastJob LastJob { get; set; }
        public TickerPromiseFix Fix { get; set; }
        /// <summary>When a check first reported this finding.</summary>
        public DateTime DetectedAtUtc { get; set; }
        public TickerPromiseDismissed Dismissed { get; set; }
    }

    internal sealed class TickerPromiseLastJob
    {
        public Guid Id { get; set; }
        public TickerStatus Status { get; set; }
        public string Error { get; set; }
    }

    internal sealed class TickerPromiseDismissed
    {
        public string By { get; set; }
        public DateTime At { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class TickerPromiseCounts
    {
        /// <summary>Rows in the window whose time limit has passed.</summary>
        public int Watched { get; set; }
        public int Done { get; set; }
        public int Running { get; set; }
        /// <summary>Rows whose latest job failed. They are not findings.</summary>
        public int Failed { get; set; }
        /// <summary>Findings nobody has dismissed.</summary>
        public int NeedsReview { get; set; }
        /// <summary>Of <see cref="NeedsReview"/>, the rows that ran or will run twice; the rest were missed.</summary>
        public int Duplicates { get; set; }
        public int Dismissed { get; set; }
        /// <summary>Rows with an empty matchBy value, which no job can match.</summary>
        public int Unmatchable { get; set; }
    }

    internal sealed class TickerPromiseActivity
    {
        public DateTime AtUtc { get; set; }
        /// <summary>fix, fix_all, auto_fix, auto_fix_paused, dismiss or settings.</summary>
        public string Kind { get; set; }
        public string Text { get; set; }
    }

    /// <summary>A promise's saved state: one definitions-store row per promise, updated with optimistic concurrency.</summary>
    internal sealed class TickerPromiseState
    {
        /// <summary>When the promise's state was first stored, which orders promises in the dashboard.</summary>
        public DateTime? CreatedAtUtc { get; set; }
        public TickerPromiseSettings Settings { get; set; } = new();
        public List<TickerPromiseDismissal> Dismissals { get; set; } = new();
        /// <summary>The node checking the promise on schedule. Only it fixes automatically and publishes events.</summary>
        public TickerPromiseLease Lease { get; set; }
        public TickerPromiseResult Result { get; set; }
        /// <summary>What the lease holder last published, so manual checks cannot hide a transition.</summary>
        public TickerPromisePublished Published { get; set; }
        public List<TickerPromiseActivity> Activity { get; set; } = new();
    }

    /// <summary>Dashboard overrides of the code.</summary>
    internal sealed class TickerPromiseSettings
    {
        public bool Paused { get; set; }
        /// <summary>When the promise was last enabled again; its next check counts from here, not its old result.</summary>
        public DateTime? EnabledAtUtc { get; set; }
        public int? WithinSeconds { get; set; }
        public bool? AutoFix { get; set; }
    }

    internal sealed class TickerPromiseDismissal
    {
        public Guid FindingId { get; set; }
        public string By { get; set; }
        public DateTime At { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class TickerPromiseLease
    {
        public string Holder { get; set; }
        public DateTime UntilUtc { get; set; }
    }

    internal sealed class TickerPromisePublished
    {
        public int NeedsReview { get; set; }
        public string Error { get; set; }
        public DateTime AtUtc { get; set; }
    }

    internal sealed class TickerPromiseResult
    {
        public DateTime CheckedAtUtc { get; set; }
        public long DurationMs { get; set; }
        public string Error { get; set; }
        /// <summary>
        /// More rows were due than one check reads. Findings and the running and failed counts cover the newest rows;
        /// Watched, and Done when the promise has DoneWhen, cover every row.
        /// </summary>
        public bool Truncated { get; set; }
        public DateTime WindowStartUtc { get; set; }
        public TickerPromiseCounts Counts { get; set; } = new();
        /// <summary>The newest findings, at most 200.</summary>
        public List<TickerPromiseFinding> Findings { get; set; } = new();
        public bool AutoFixPaused { get; set; }
        public string AutoFixPausedReason { get; set; }
    }

    /// <summary>A promise and its latest check, as the dashboard shows it.</summary>
    internal sealed class TickerPromiseSnapshot
    {
        /// <summary>Stable id used in routes.</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public string Function { get; set; }
        public string RowType { get; set; }
        /// <summary>The promise in one sentence, for example "Each Order → ProcessOrderJob within 5 minutes after PaidAt".</summary>
        public string Summary { get; set; }
        /// <summary>DoneWhen in words, such as "Status is Sent"; null when the promise has none.</summary>
        public string DoneWhen { get; set; }
        /// <summary>How the promise is set up in code, with the options its checks run under.</summary>
        public TickerPromiseRule Rule { get; set; }
        /// <summary>When the promise's state was first stored; null until its first check or setting.</summary>
        public DateTime? CreatedAtUtc { get; set; }
        /// <summary>Whether rows whose job ran, or will run, more than once are listed; false after AllowRepeats().</summary>
        public bool ChecksDuplicates { get; set; }
        /// <summary>Where the promise is declared. Always "Code".</summary>
        public string Source { get; set; } = "Code";
        public TickerPromiseAutoFixView AutoFix { get; set; }
        public TickerPromiseSettingsView Settings { get; set; }
        public DateTime? CheckedAtUtc { get; set; }
        /// <summary>When the next scheduled check is due; null while paused. Checks run on nodes that run jobs.</summary>
        public DateTime? NextCheckAtUtc { get; set; }
        public int CheckIntervalSeconds { get; set; }
        public long? DurationMs { get; set; }
        public string Error { get; set; }
        public bool Truncated { get; set; }
        public DateTime? WindowStartUtc { get; set; }
        public TickerPromiseCounts Counts { get; set; }
        public List<TickerPromiseFinding> Findings { get; set; }
        /// <summary>Newest first.</summary>
        public List<TickerPromiseActivity> Activity { get; set; }
        public bool CanManage { get; set; }
    }

    /// <summary>
    /// A promise as the developer declared it, call by call, for the dashboard to show. The rows query and the job's
    /// request are compiled code, so they are described by their types rather than shown.
    /// </summary>
    internal sealed class TickerPromiseRule
    {
        public TickerPromiseDeclaration DeclaredOn { get; set; }
        public string RowType { get; set; }
        /// <summary>The service the rows are read from, such as "BillingDbContext"; null when read from the service provider.</summary>
        public string RowsFrom { get; set; }
        /// <summary>The function class registered with MapTicker, such as "ProcessOrderJob"; null for a function referenced by name.</summary>
        public string FunctionClass { get; set; }
        public string RequestType { get; set; }
        public List<string> MatchBy { get; set; } = new();
        public int WithinSeconds { get; set; }
        public string After { get; set; }
        /// <summary>DoneWhen in words; null when a row is done once one of its jobs succeeds.</summary>
        public string DoneWhen { get; set; }
        public bool HasLabel { get; set; }
        /// <summary>The name given with Named(); null when the promise uses its default name.</summary>
        public string NamedInCode { get; set; }
        /// <summary>AutoFix()'s limit per check; null when the code leaves fixing to a person.</summary>
        public int? AutoFixMax { get; set; }
        public bool AllowRepeats { get; set; }
        public TickerPromiseCheckOptions Checks { get; set; }
    }

    /// <summary>The application-wide options every promise is checked with.</summary>
    internal sealed class TickerPromiseCheckOptions
    {
        public int CheckIntervalSeconds { get; set; }
        public int NeverRanAfterSeconds { get; set; }
        public int MaxRowsPerCheck { get; set; }
        public int MaxJobsPerCheck { get; set; }
        /// <summary>How far back rows are checked, in days.</summary>
        public double LookBackDays { get; set; }
        /// <summary>The look-back comes from job retention, so a row is never reported because its job was deleted.</summary>
        public bool LookBackFromRetention { get; set; }
    }

    internal sealed class TickerPromiseAutoFixView
    {
        public bool Enabled { get; set; }
        /// <summary>Whether code turned automatic fixing on with AutoFix().</summary>
        public bool FromCode { get; set; }
        public int MaxPerCheck { get; set; }
        public bool Paused { get; set; }
        public string PausedReason { get; set; }
    }

    internal sealed class TickerPromiseSettingsView
    {
        public bool Paused { get; set; }
        /// <summary>The dashboard's time limit, or null when the code's applies.</summary>
        public int? WithinSeconds { get; set; }
        public int WithinSecondsFromCode { get; set; }
    }

    /// <summary>A settings change; a null property is left unchanged unless its Set flag says otherwise.</summary>
    internal sealed class TickerPromiseSettingsChange
    {
        public bool? Paused { get; init; }
        public bool SetWithinSeconds { get; init; }
        public int? WithinSeconds { get; init; }
        public bool SetAutoFix { get; init; }
        public bool? AutoFix { get; init; }
    }

    internal sealed class TickerPromiseFixResult
    {
        public Guid JobId { get; set; }
        public TickerPromiseFix Fix { get; set; }
        public TickerPromiseSnapshot Promise { get; set; }
    }

    internal sealed class TickerPromiseFixAllResult
    {
        public int Fixed { get; set; }
        public int Failed { get; set; }
        /// <summary>Fixable findings left for another run, beyond the 100 one run fixes.</summary>
        public int Remaining { get; set; }
        public TickerPromiseSnapshot Promise { get; set; }
    }

    /// <summary>Who scheduled a job of the promise's function.</summary>
    internal enum TickerPromiseJobSource
    {
        /// <summary>The application, or anyone else outside the promise.</summary>
        App,
        /// <summary>A person ran the promise's fix.</summary>
        Fix,
        /// <summary>The promise fixed the row automatically.</summary>
        AutoFix,
    }

    /// <summary>One job of the promise's function, as the dashboard lists it.</summary>
    internal sealed class TickerPromiseJob
    {
        public Guid Id { get; set; }
        public TickerStatus Status { get; set; }
        /// <summary>The job's match values, as findings show them; null when its request has none or can't be read.</summary>
        public string Key { get; set; }
        /// <summary>The row's label for a fix job, otherwise the job's description.</summary>
        public string Label { get; set; }
        public TickerPromiseJobSource Source { get; set; }
        /// <summary>The person who ran the fix, for <see cref="TickerPromiseJobSource.Fix"/>.</summary>
        public string FixedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ExecutionTimeUtc { get; set; }
        public DateTime? ExecutedAtUtc { get; set; }
        public long ElapsedMs { get; set; }
        public int RetryCount { get; set; }
        public int Retries { get; set; }
        /// <summary>The failure's message, or why the job was skipped.</summary>
        public string Error { get; set; }
    }

    /// <summary>The newest jobs of the promise's function since its window started, with totals for the whole window.</summary>
    internal sealed class TickerPromiseJobList
    {
        public DateTime WindowStartUtc { get; set; }
        public TickerPromiseJobTotals Totals { get; set; } = new();
        /// <summary>Newest first, at most 100.</summary>
        public List<TickerPromiseJob> Jobs { get; set; } = new();
    }

    internal sealed class TickerPromiseJobTotals
    {
        public int Done { get; set; }
        public int Failed { get; set; }
        public int Running { get; set; }
        /// <summary>Idle or queued.</summary>
        public int Waiting { get; set; }
        /// <summary>Cancelled or skipped.</summary>
        public int Stopped { get; set; }
    }

    /// <summary>The function's jobs per day for the dashboard's charts, with who scheduled them and how long they ran.</summary>
    internal sealed class TickerPromiseAnalytics
    {
        public string PromiseId { get; set; }
        /// <summary>The first day's midnight in the caller's offset, as UTC.</summary>
        public DateTime FromUtc { get; set; }
        /// <summary>More jobs than one read takes were created in the range, so the oldest days count too few.</summary>
        public bool Truncated { get; set; }
        /// <summary>One entry per day, oldest first, empty days included.</summary>
        public List<TickerPromiseDay> Daily { get; set; } = new();
        public TickerPromiseSourceCounts Sources { get; set; } = new();
        public long? AverageMs { get; set; }
        public long? P95Ms { get; set; }
    }

    internal sealed class TickerPromiseDay
    {
        /// <summary>The day in the caller's offset, yyyy-MM-dd.</summary>
        public string Day { get; set; }
        public int Done { get; set; }
        public int Failed { get; set; }
        /// <summary>Cancelled or skipped.</summary>
        public int Stopped { get; set; }
        /// <summary>Idle, queued or running.</summary>
        public int Pending { get; set; }
        /// <summary>Jobs a person's fix created.</summary>
        public int Fixes { get; set; }
        public int AutoFixes { get; set; }
        public long? AverageMs { get; set; }
        public long? P95Ms { get; set; }
    }

    internal sealed class TickerPromiseSourceCounts
    {
        public int App { get; set; }
        public int Fix { get; set; }
        public int AutoFix { get; set; }
    }

    /// <summary>A promise request that cannot be done, with the HTTP status and the message to show.</summary>
    internal sealed class TickerPromiseException(int statusCode, string message) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    internal static class TickerPromiseIds
    {
        /// <summary>
        /// The id of a promise's definitions-store row. It includes the runtime partition because some stores, such as
        /// MongoDB, key rows by id alone, so two partitions with a promise of the same name must not share one.
        /// </summary>
        public static Guid StateRow(string partitionStorageKey, string promiseName)
            => Create("row", partitionStorageKey, Models.SavedTickerDefinitionNames.Normalize(promiseName));

        /// <summary>A GUID derived from <paramref name="parts"/>, the same on every node.</summary>
        public static Guid Create(params string[] parts)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes("tickerq-promise|" + string.Join('|', parts)));
            var bytes = hash.AsSpan(0, 16).ToArray();
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes);
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(TickerPromiseState))]
    internal partial class TickerPromiseJsonContext : JsonSerializerContext;
}
