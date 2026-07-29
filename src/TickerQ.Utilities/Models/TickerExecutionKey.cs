using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

internal readonly record struct TickerExecutionKey(TickerType Type, Guid TickerId);
