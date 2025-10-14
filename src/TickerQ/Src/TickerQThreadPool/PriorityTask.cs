using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.TickerQThreadPool;

/// <summary>
/// Compressed priority task structure - optimized for memory
/// </summary>
public readonly struct PriorityTask
{
    private readonly byte _priorityAndFlags;
    public readonly Func<CancellationToken, Task> Work;
    public readonly CancellationToken UserToken;
    private readonly uint _queueTimeMs;
    private readonly bool _shouldDecrementTotal;

    private static readonly DateTime StartTime = DateTime.UtcNow;

    public PriorityTask(TickerTaskPriority priority, Func<CancellationToken, Task> work, CancellationToken userToken, bool shouldDecrementTotal = true)
    {
        _priorityAndFlags = (byte)priority;
        Work = work;
        UserToken = userToken;
        _queueTimeMs = (uint)(DateTime.UtcNow - StartTime).TotalMilliseconds;
        _shouldDecrementTotal = shouldDecrementTotal;
    }

    public TickerTaskPriority Priority => (TickerTaskPriority)(_priorityAndFlags & 0x03);
    public DateTime QueueTime => StartTime.AddMilliseconds(_queueTimeMs);
    public bool ShouldDecrementTotal => _shouldDecrementTotal;
}