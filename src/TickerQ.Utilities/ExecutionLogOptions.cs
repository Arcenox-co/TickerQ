using System;
using Microsoft.Extensions.Logging;

namespace TickerQ.Utilities;

/// <summary>Bounded diagnostic log tails. Database persistence is opt-in and best effort.</summary>
public sealed class ExecutionLogOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Include scheduler lifecycle events in the same bounded tail as application logs.</summary>
    public bool IncludeLifecycleEvents { get; set; } = true;
    public bool PersistToDatabase { get; set; }
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public int MaxLinesPerExecution { get; set; } = 200;
    public int MaxMessageBytes { get; set; } = 2048;
    public int MaxBytesPerExecution { get; set; } = 32 * 1024;
    public int MaxBufferedExecutions { get; set; } = 500;
    public int MaxBufferedBytes { get; set; } = 16 * 1024 * 1024;
    public TimeSpan MemoryRetention { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
    public int MaxStoredExecutions { get; set; } = 5000;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);
    public int WriteBatchSize { get; set; } = 100;
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan StorageTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Optional application redaction, applied before buffering or persistence. Throwing drops the line.</summary>
    public Func<string, string> Redact { get; set; }

    public void Validate()
    {
        if (MinimumLevel < LogLevel.Trace || MinimumLevel > LogLevel.None)
            throw new ArgumentOutOfRangeException(nameof(MinimumLevel));
        Bound(MaxLinesPerExecution, 1, 1000, nameof(MaxLinesPerExecution));
        Bound(MaxMessageBytes, 128, Math.Min(16384, MaxBytesPerExecution - 1024), nameof(MaxMessageBytes));
        Bound(MaxBytesPerExecution, 4096, 262144, nameof(MaxBytesPerExecution));
        Bound(MaxBufferedExecutions, 1, 10000, nameof(MaxBufferedExecutions));
        Bound(MaxBufferedBytes, MaxBytesPerExecution, 268435456, nameof(MaxBufferedBytes));
        Bound(MaxStoredExecutions, 1, 1000000, nameof(MaxStoredExecutions));
        Bound(WriteBatchSize, 1, 500, nameof(WriteBatchSize));
        Duration(MemoryRetention, TimeSpan.FromSeconds(1), TimeSpan.FromDays(1), nameof(MemoryRetention));
        Duration(Retention, TimeSpan.FromMinutes(1), TimeSpan.FromDays(365), nameof(Retention));
        Duration(FlushInterval, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1), nameof(FlushInterval));
        Duration(CleanupInterval, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), nameof(CleanupInterval));
        Duration(StorageTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1), nameof(StorageTimeout));
        if (PersistToDatabase && !Enabled)
            throw new InvalidOperationException("Enable execution log capture before enabling database persistence.");
    }

    private static void Bound(int value, int min, int max, string name)
    { if (value < min || value > max) throw new ArgumentOutOfRangeException(name, $"Must be between {min} and {max}."); }
    private static void Duration(TimeSpan value, TimeSpan min, TimeSpan max, string name)
    { if (value < min || value > max) throw new ArgumentOutOfRangeException(name, $"Must be between {min} and {max}."); }
}
