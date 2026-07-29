using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Entities;

/// <summary>
/// Durable, secret-free Node finalization intent plus leased delivery state. This row deliberately
/// has no navigation or foreign key to a ticker because terminal ticker retention must not remove it.
/// </summary>
public sealed class NodeFinalizationOutboxEntity
{
    public Guid OutboxId { get; set; }
    public int SchemaVersion { get; set; }
    public TickerType TickerType { get; set; }
    public Guid TickerId { get; set; }
    public Guid AcquisitionToken { get; set; }
    public Guid DispatchId { get; set; }
    public Guid NodeEpoch { get; set; }
    public string FinalizeUri { get; set; }
    public string FinalizePathAndQuery { get; set; }
    public bool AllowPrivateCallbackAddressesForLocalDevelopment { get; set; }
    public Guid RequestNonce { get; set; }
    public Guid ControlNonce { get; set; }
    public byte[] ExactBody { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public DateTime AvailableAtUtc { get; set; }
    public Guid? ClaimToken { get; set; }
    public string ClaimedBy { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public string LastErrorCode { get; set; }
}
