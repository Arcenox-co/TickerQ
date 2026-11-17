using System;

namespace TickerQ.Utilities.Models;

/// <summary>
/// One promise's stored state: its latest check, dismissals, dashboard settings and lease, as JSON. One row per
/// promise and application namespace, changed only at its current version, so every node reads and writes the
/// same state.
/// </summary>
public sealed class TickerPromiseStateRow
{
    /// <summary>Derived from the application namespace and the promise name, so two applications never share a row.</summary>
    public Guid Id { get; set; }
    public string ApplicationNamespaceKey { get; set; } = string.Empty;
    /// <summary>The promise's name, for people reading the table; at most 200 characters.</summary>
    public string Name { get; set; } = string.Empty;
    public string StateJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; } = 1;

    public TickerPromiseStateRow Copy() => (TickerPromiseStateRow)MemberwiseClone();
}
