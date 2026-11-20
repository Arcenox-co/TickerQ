using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces;

/// <summary>
/// Optional operational-store capability that keeps each promise's state in a table of its own, with a column large
/// enough for the whole state on every database. A provider without it keeps promise state in the definitions store
/// (<see cref="ITickerDefinitionStore"/>) instead, shortened where that store's column is bounded.
/// </summary>
public interface ITickerPromiseStateStore
{
    /// <summary>
    /// False when the provider has this store but its table is not there, so promises keep their state in the
    /// definitions store instead. Built-in providers always have the table once their schema is current.
    /// </summary>
    bool HasPromiseStateTable => true;

    /// <summary>Every promise's row in this application namespace, oldest first.</summary>
    Task<IReadOnlyList<TickerPromiseStateRow>> ListPromiseStatesAsync(CancellationToken cancellationToken = default);

    Task<TickerPromiseStateRow> GetPromiseStateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stores a new row at version 1. Throws when a row with the id already exists.</summary>
    Task<TickerPromiseStateRow> CreatePromiseStateAsync(TickerPromiseStateRow row, CancellationToken cancellationToken = default);

    /// <summary>Replaces the row's state and name at <paramref name="expectedVersion"/>; null when that version is gone.</summary>
    Task<TickerPromiseStateRow> UpdatePromiseStateAsync(TickerPromiseStateRow row, long expectedVersion,
        CancellationToken cancellationToken = default);
}
