using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces;

/// <summary>
/// Optional operational-store capability for dashboard templates and drafts. These records are
/// definitions, not scheduler rows, and must never participate in acquisition or retention queries.
/// </summary>
public interface ITickerDefinitionStore
{
    Task<IReadOnlyList<SavedTickerDefinition>> ListDefinitionsAsync(
        SavedTickerDefinitionKind? kind = null,
        SavedTickerDefinitionTarget? target = null,
        CancellationToken cancellationToken = default);

    Task<SavedTickerDefinition> GetDefinitionAsync(Guid id,
        CancellationToken cancellationToken = default);

    Task<SavedTickerDefinition> CreateDefinitionAsync(SavedTickerDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>Returns null when the expected version lost an optimistic concurrency race.</summary>
    Task<SavedTickerDefinition> UpdateDefinitionAsync(SavedTickerDefinition definition,
        long expectedVersion, CancellationToken cancellationToken = default);

    Task<bool> DeleteDefinitionAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims an unactivated time draft. Returns null when it is missing, already activated/claimed,
    /// is not a time draft, or the expected version is stale.
    /// </summary>
    Task<SavedTickerDefinition> TryClaimDraftActivationAsync(Guid id, long expectedVersion,
        Guid activationToken, Guid pendingRootTickerId, DateTime claimedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteDraftActivationAsync(Guid id, Guid activationToken,
        Guid rootTickerId, DateTime activatedAtUtc, CancellationToken cancellationToken = default);

    Task<bool> ReleaseDraftActivationAsync(Guid id, Guid activationToken,
        CancellationToken cancellationToken = default);
}
