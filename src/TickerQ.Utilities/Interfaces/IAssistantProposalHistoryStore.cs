using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Optional extension for history stores that persist assistant proposal cards and
    /// their confirmation state. Keeping this separate preserves compatibility with
    /// existing text-only <see cref="IAssistantHistoryStore"/> implementations.
    /// </summary>
    public interface IAssistantProposalHistoryStore
    {
        Task<Guid> AppendExchangeWithProposalsAsync(
            string userId,
            Guid? conversationId,
            string userMessage,
            string assistantMessage,
            string model,
            IReadOnlyList<AssistantHistoryProposal> proposals,
            CancellationToken cancellationToken = default);

        /// <summary>Updates a proposal owned by the authenticated user.</summary>
        Task<bool> SetProposalStateAsync(
            string userId,
            Guid proposalId,
            string status,
            string resultJson,
            CancellationToken cancellationToken = default);
    }
}
