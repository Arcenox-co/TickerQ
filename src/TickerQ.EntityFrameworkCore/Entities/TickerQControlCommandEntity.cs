using System;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Entities;

public sealed class TickerQControlCommandEntity
{
    public string ApplicationNamespaceKey { get; set; }
    public Guid Id { get; set; }
    public string TargetNodeKey { get; set; }
    public string TargetExecutionOwnerId { get; set; }
    public TickerType TickerType { get; set; }
    public Guid TickerId { get; set; }
    public Guid? AcquisitionToken { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public TickerQControlCommandStatus Status { get; set; }
    public string Outcome { get; set; }
}
