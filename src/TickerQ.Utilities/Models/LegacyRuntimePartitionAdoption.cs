using System;

namespace TickerQ.Utilities.Models;

/// <summary>Explicit, immutable authority to claim the namespace-less legacy runtime store.</summary>
public sealed record LegacyRuntimePartitionAdoption
{
    public LegacyRuntimePartitionAdoption(
        TickerQRuntimePartition targetPartition, long epoch, bool legacyWritersDrained)
    {
        TargetPartition = targetPartition ?? throw new ArgumentNullException(nameof(targetPartition));
        if (targetPartition.IsLegacyGlobal)
            throw new ArgumentException("Legacy runtime adoption requires a non-legacy target partition.", nameof(targetPartition));
        if (epoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(epoch), epoch, "Legacy runtime adoption epoch must be positive.");
        if (!legacyWritersDrained)
            throw new ArgumentException(
                "Legacy runtime adoption requires positive confirmation that all pre-partition writers are drained.",
                nameof(legacyWritersDrained));
        Epoch = epoch;
        LegacyWritersDrained = true;
    }

    public TickerQRuntimePartition TargetPartition { get; }
    public long Epoch { get; }
    public bool LegacyWritersDrained { get; }
}
