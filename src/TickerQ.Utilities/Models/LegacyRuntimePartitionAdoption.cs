using System;

namespace TickerQ.Utilities.Models;

/// <summary>Explicit, immutable authority to claim the namespace-less legacy runtime store.</summary>
public sealed record LegacyRuntimePartitionAdoption
{
    public LegacyRuntimePartitionAdoption(TickerQRuntimePartition targetPartition, long epoch)
    {
        TargetPartition = targetPartition ?? throw new ArgumentNullException(nameof(targetPartition));
        if (targetPartition.IsLegacyGlobal)
            throw new ArgumentException("Legacy runtime adoption requires a non-legacy target partition.", nameof(targetPartition));
        if (epoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(epoch), epoch, "Legacy runtime adoption epoch must be positive.");
        Epoch = epoch;
    }

    public TickerQRuntimePartition TargetPartition { get; }
    public long Epoch { get; }
}
