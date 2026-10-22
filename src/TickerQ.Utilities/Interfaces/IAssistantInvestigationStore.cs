using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Optional durable storage for AI run investigations. Implementations must scope every
    /// operation by application namespace and atomically deduplicate an execution in
    /// <see cref="TryStartAsync"/> so multiple scheduler nodes cannot purchase the same diagnosis.
    /// </summary>
    public interface IAssistantInvestigationStore
    {
        /// <summary>
        /// Claims one execution for investigation. Returns the new record id, or null when that
        /// execution already has an investigation in the namespace.
        /// </summary>
        Task<Guid?> TryStartAsync(
            AssistantInvestigationRecord investigation,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the newest completed diagnosis with the same evidence fingerprint. Automatic
        /// investigators use this to copy an existing diagnosis to a new recurring execution
        /// without purchasing another model response. Implementations must scope the lookup by
        /// application namespace and exclude dismissed records.
        /// </summary>
        Task<AssistantInvestigationRecord> FindReusableAsync(
            string applicationNamespace,
            string evidenceFingerprint,
            Guid executionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AssistantInvestigationRecord>(null);

        Task CompleteAsync(
            string applicationNamespace,
            Guid investigationId,
            string summary,
            string model,
            Guid? conversationId = null,
            CancellationToken cancellationToken = default);

        Task FailAsync(
            string applicationNamespace,
            Guid investigationId,
            string failureMessage,
            CancellationToken cancellationToken = default);

        Task<AssistantInvestigationPage> ListAsync(
            string applicationNamespace,
            AssistantInvestigationQuery query,
            CancellationToken cancellationToken = default);

        Task<AssistantInvestigationRecord> GetAsync(
            string applicationNamespace,
            Guid investigationId,
            CancellationToken cancellationToken = default);

        Task<AssistantInvestigationOverview> GetOverviewAsync(
            string applicationNamespace,
            CancellationToken cancellationToken = default);

        Task<bool> SetReviewStateAsync(
            string applicationNamespace,
            Guid investigationId,
            string reviewState,
            string reviewedByUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes one saved investigation in the application namespace. Implementations that
        /// support automatic investigations must retain an internal execution tombstone so a
        /// later sweep cannot recreate the deleted diagnosis.
        /// </summary>
        Task<bool> DeleteAsync(
            string applicationNamespace,
            Guid investigationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        /// <summary>
        /// Deletes every saved investigation for a function in the application namespace while
        /// retaining execution tombstones needed to suppress automatic reprocessing.
        /// </summary>
        Task<int> DeleteByFunctionAsync(
            string applicationNamespace,
            string functionName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        /// <summary>Marks older unresolved investigations of the function as recovered.</summary>
        Task<int> MarkRecoveredAsync(
            string applicationNamespace,
            string functionName,
            Guid successfulExecutionId,
            DateTime successfulAt,
            CancellationToken cancellationToken = default);
    }
}
