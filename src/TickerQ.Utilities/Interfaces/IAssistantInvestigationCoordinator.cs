using System;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Durable, namespace-scoped coordination for automatic AI investigations. Implementations
    /// must make lease acquisition and model-call reservation atomic across every scheduler node
    /// that shares the operational store.
    /// </summary>
    public interface IAssistantInvestigationCoordinator
    {
        /// <summary>
        /// Acquires the exclusive lease for one evidence fingerprint, or returns false while
        /// another node owns an unexpired lease.
        /// </summary>
        Task<bool> TryAcquireEvidenceLeaseAsync(
            string applicationNamespace,
            string evidenceFingerprint,
            Guid ownerToken,
            DateTime leaseUntilUtc,
            CancellationToken cancellationToken = default);

        /// <summary>Extends a lease only when it is still owned by <paramref name="ownerToken"/>.</summary>
        Task<bool> RenewEvidenceLeaseAsync(
            string applicationNamespace,
            string evidenceFingerprint,
            Guid ownerToken,
            DateTime leaseUntilUtc,
            CancellationToken cancellationToken = default);

        /// <summary>Releases a lease only when it is still owned by <paramref name="ownerToken"/>.</summary>
        Task ReleaseEvidenceLeaseAsync(
            string applicationNamespace,
            string evidenceFingerprint,
            Guid ownerToken,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically reserves one model-call start in the namespace-wide rolling one-hour
        /// window. Returns false when the shared limit has already been reached.
        /// </summary>
        Task<bool> TryReserveModelCallAsync(
            string applicationNamespace,
            DateTime startedAtUtc,
            int maximumPerHour,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the stale processing claim for an execution after its evidence lease has
        /// expired. This lets another node recover work abandoned by a terminated owner.
        /// </summary>
        Task AbandonStaleProcessingAsync(
            string applicationNamespace,
            Guid executionId,
            DateTime notUpdatedSinceUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a processing claim that has not called the model because the shared budget
        /// was exhausted. Completed, failed, dismissed, and foreign-namespace rows are preserved.
        /// </summary>
        Task AbandonProcessingAsync(
            string applicationNamespace,
            Guid investigationId,
            CancellationToken cancellationToken = default);
    }
}
