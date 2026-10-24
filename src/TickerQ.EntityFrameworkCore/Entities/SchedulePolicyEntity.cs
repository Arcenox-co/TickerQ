using System;

namespace TickerQ.EntityFrameworkCore.Entities;

public sealed class SchedulePolicyEntity
{
    public string ApplicationNamespaceKey { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string NameKey { get; set; }
    public string Description { get; set; }
    public string DefinitionJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
