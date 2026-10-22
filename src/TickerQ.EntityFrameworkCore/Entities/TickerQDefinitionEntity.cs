using System;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Entities;

public sealed class TickerQDefinitionEntity
{
    public string ApplicationNamespaceKey { get; set; }
    public Guid Id { get; set; }
    public SavedTickerDefinitionKind Kind { get; set; }
    public SavedTickerDefinitionTarget Target { get; set; }
    public string Name { get; set; }
    public string NameKey { get; set; }
    public string Description { get; set; }
    public string DefinitionJson { get; set; }
    public bool IncludesRequestData { get; set; }
    public int SchemaVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    public Guid? ActivationToken { get; set; }
    public Guid? PendingRootTickerId { get; set; }
    public DateTime? ActivationClaimedAtUtc { get; set; }
    public Guid? ActivatedRootTickerId { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
}
