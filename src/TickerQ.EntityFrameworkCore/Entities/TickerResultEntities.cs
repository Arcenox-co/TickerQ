using System;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Entities;

/// <summary>
/// Additive one-to-one result row for a time-ticker execution. Keeping results outside the
/// polymorphic ticker hierarchy makes existing ticker tables migration-compatible.
/// </summary>
public sealed class TimeTickerResultEntity<TTimeTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
{
    public Guid TickerId { get; set; }
    public byte[] Payload { get; set; }
    public int EnvelopeVersion { get; set; }
    public string MediaType { get; set; }
    public string ContractId { get; set; }
    public string ContractType { get; set; }
    public TTimeTicker Ticker { get; set; }
}

/// <summary>Additive one-to-one result row for a cron-ticker occurrence (never its definition).</summary>
public sealed class CronTickerOccurrenceResultEntity<TCronTicker>
    where TCronTicker : CronTickerEntity, new()
{
    public Guid TickerId { get; set; }
    public byte[] Payload { get; set; }
    public int EnvelopeVersion { get; set; }
    public string MediaType { get; set; }
    public string ContractId { get; set; }
    public string ContractType { get; set; }
    public CronTickerOccurrenceEntity<TCronTicker> Occurrence { get; set; }
}