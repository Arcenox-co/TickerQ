using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

internal readonly record struct TickerExecutionKey(string RuntimePartitionKey, TickerType Type, Guid TickerId)
{
    internal TickerExecutionKey(TickerType type, Guid tickerId)
        : this(TickerQRuntimePartition.LegacyGlobal.StorageKey, type, tickerId) { }
}
