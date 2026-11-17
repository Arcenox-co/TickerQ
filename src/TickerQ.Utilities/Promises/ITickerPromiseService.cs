using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Promises
{
    /// <summary>
    /// Checks promises and acts on their findings. State lives in the operational store's definitions, one row per
    /// promise, so every node reads the same results. Ids are the snapshot <see cref="TickerPromiseSnapshot.Id"/>s.
    /// Failures meant for the caller are <see cref="TickerPromiseException"/>s.
    /// </summary>
    internal interface ITickerPromiseService
    {
        /// <summary>True when at least one promise is declared.</summary>
        bool HasPromises { get; }

        /// <summary>Every promise with its latest stored result.</summary>
        Task<IReadOnlyList<TickerPromiseSnapshot>> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>Checks the promise on this node now, stores the result and returns it.</summary>
        Task<TickerPromiseSnapshot> CheckAsync(string id, CancellationToken cancellationToken = default);

        Task<TickerPromiseSnapshot> UpdateSettingsAsync(string id, TickerPromiseSettingsChange change, string user,
            CancellationToken cancellationToken = default);

        /// <summary>Checks the promise again, then runs the finding's fix. Fails with 409 when the finding is gone.</summary>
        Task<TickerPromiseFixResult> FixAsync(string id, Guid findingId, string user, CancellationToken cancellationToken = default);

        /// <summary>Checks the promise again, then fixes up to 100 open, fixable findings.</summary>
        Task<TickerPromiseFixAllResult> FixAllAsync(string id, string user, CancellationToken cancellationToken = default);

        /// <summary>The newest jobs of the promise's function in its window, which jobs a fix created, and totals.</summary>
        Task<TickerPromiseJobList> JobsAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Each promise's jobs per day over the last <paramref name="days"/> days (1 to 90), in the caller's UTC offset,
        /// or only <paramref name="id"/>'s when given.
        /// </summary>
        Task<IReadOnlyList<TickerPromiseAnalytics>> AnalyticsAsync(string id, int days, int offsetMinutes,
            CancellationToken cancellationToken = default);

        Task<TickerPromiseSnapshot> DismissAsync(string id, Guid findingId, string user, string reason,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The scheduled pass: checks every due, unpaused promise whose lease this node can take, fixes automatically
        /// where enabled, stores the result and publishes events for transitions.
        /// </summary>
        Task CheckDueAsync(CancellationToken cancellationToken = default);
    }
}
