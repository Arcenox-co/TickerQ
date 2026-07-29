#nullable disable
using System.Security.Cryptography;
using System.Text.Json;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed class RedisNodeFinalizationRecord
{
    public int SchemaVersion { get; set; }
    public string OutboxId { get; set; }
    public int TickerType { get; set; }
    public string TickerId { get; set; }
    public string AcquisitionToken { get; set; }
    public string DispatchId { get; set; }
    public string NodeEpoch { get; set; }
    public string FinalizeUri { get; set; }
    public string FinalizePathAndQuery { get; set; }
    public bool AllowPrivateCallbackAddressesForLocalDevelopment { get; set; }
    public string RequestNonce { get; set; }
    public string ControlNonce { get; set; }
    public string ExactBodyBase64 { get; set; }
    public string BodyDigest { get; set; }
    public string ImmutableDigest { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public string ClaimToken { get; set; }
    public string ClaimedBy { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public string LastErrorCode { get; set; }

    internal static RedisNodeFinalizationRecord Create(NodeFinalizationIntent intent)
    {
        var body = intent.ExactBody;
        var record = new RedisNodeFinalizationRecord
        {
            SchemaVersion = intent.SchemaVersion,
            OutboxId = intent.OutboxId.ToString("D"),
            TickerType = (int)intent.TickerType,
            TickerId = intent.TickerId.ToString("D"),
            AcquisitionToken = intent.AcquisitionToken.ToString("D"),
            DispatchId = intent.DispatchId.ToString("D"),
            NodeEpoch = intent.NodeEpoch.ToString("D"),
            FinalizeUri = intent.FinalizeUri,
            FinalizePathAndQuery = intent.FinalizePathAndQuery,
            AllowPrivateCallbackAddressesForLocalDevelopment = intent.AllowPrivateCallbackAddressesForLocalDevelopment,
            RequestNonce = intent.RequestNonce.ToString("D"),
            ControlNonce = intent.ControlNonce.ToString("D"),
            ExactBodyBase64 = Convert.ToBase64String(body),
            BodyDigest = Convert.ToHexString(SHA256.HashData(body)),
            CreatedAtUtc = intent.CreatedAtUtc,
            AvailableAtUtc = intent.CreatedAtUtc,
            AttemptCount = 0
        };
        record.ImmutableDigest = ComputeImmutableDigest(record);
        return record;
    }

    internal NodeFinalizationIntent ToIntent()
        => new(SchemaVersion, Guid.Parse(OutboxId), (TickerType)TickerType, Guid.Parse(TickerId),
            Guid.Parse(AcquisitionToken), Guid.Parse(DispatchId), Guid.Parse(NodeEpoch), FinalizeUri,
            FinalizePathAndQuery, AllowPrivateCallbackAddressesForLocalDevelopment,
            Guid.Parse(RequestNonce), Guid.Parse(ControlNonce), Convert.FromBase64String(ExactBodyBase64), CreatedAtUtc);

    internal bool HasValidIntegrity()
    {
        try
        {
            var bodyDigest = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(ExactBodyBase64)));
            return string.Equals(BodyDigest, bodyDigest, StringComparison.Ordinal) &&
                   string.Equals(ImmutableDigest, ComputeImmutableDigest(this), StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ComputeImmutableDigest(RedisNodeFinalizationRecord record)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", record.SchemaVersion);
            writer.WriteString("outboxId", record.OutboxId);
            writer.WriteNumber("tickerType", record.TickerType);
            writer.WriteString("tickerId", record.TickerId);
            writer.WriteString("acquisitionToken", record.AcquisitionToken);
            writer.WriteString("dispatchId", record.DispatchId);
            writer.WriteString("nodeEpoch", record.NodeEpoch);
            writer.WriteString("finalizeUri", record.FinalizeUri);
            writer.WriteString("finalizePathAndQuery", record.FinalizePathAndQuery);
            writer.WriteBoolean("allowPrivate", record.AllowPrivateCallbackAddressesForLocalDevelopment);
            writer.WriteString("requestNonce", record.RequestNonce);
            writer.WriteString("controlNonce", record.ControlNonce);
            writer.WriteString("bodyDigest", record.BodyDigest);
            writer.WriteString("createdAtUtc", record.CreatedAtUtc.ToString("O"));
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
