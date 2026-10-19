using System;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Utilities.Models;

/// <summary>One compressed, bounded tail per ticker ID and application partition, not an audit trail.</summary>
public sealed class ExecutionLogArchive
{
    public string Id { get; set; }
    public string PartitionKey { get; set; }
    public Guid TickerId { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public long ExpiresAtUtcTicks { get; set; }
    public byte[] Payload { get; set; }
    public bool Truncated { get; set; }
    public static string StorageId(string partitionKey, Guid tickerId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partitionKey + ":" + tickerId.ToString("N"))));
}
