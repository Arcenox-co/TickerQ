using System;

namespace TickerQ.EntityFrameworkCore.Entities;

/// <summary>One promise's stored state. See <c>TickerQ.Utilities.Models.TickerPromiseStateRow</c>.</summary>
public sealed class TickerQPromiseEntity
{
    public Guid Id { get; set; }
    public string ApplicationNamespaceKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StateJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
