using System;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Entities;

/// <summary>Singleton checkpoint for the installed additive schema and provider-owned data upgrades.</summary>
public sealed class TickerQStoreMetadata
{
    public const string SingletonId = "TickerQ";
    public const string TimeTickerGraphMutationSentinelId = "TickerQ:TimeTickerGraphMutation";
    public const string LegacyRuntimeAdoptionId = "TickerQ:LegacyRuntimeAdoption";

    public string ApplicationNamespaceKey { get; set; } = TickerQRuntimePartition.LegacyGlobal.StorageKey;
    public string Id { get; set; } = SingletonId;
    public int SchemaVersion { get; set; }
    public int DataVersion { get; set; }
    public string LastMigrationId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    public long ActivationEpoch { get; set; }
    public ActivationEpochPhase ActivationPhase { get; set; }
    public string ActivationCheckpoint { get; set; }
    /// <summary>Startup manifest fingerprint recorded by the deployment that claimed <see cref="ActivationEpoch"/>.</summary>
    public string ActivationManifest { get; set; }
    /// <summary>Comma-separated fingerprints of earlier deployments, newest first.</summary>
    public string ActivationManifestHistory { get; set; }
}
