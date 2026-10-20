using System;
using System.Collections.Generic;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>Builds execution graphs without consuming a stack frame per chain level.</summary>
internal static class TimeTickerContextBuilder
{
    internal static InternalFunctionContext Build(TimeTickerEntity root,
        Func<TimeTickerEntity, InternalFunctionContext> createContext)
    {
        var result = createContext(root);
        var pending = new Stack<(TimeTickerEntity Entity, InternalFunctionContext Context)>();
        pending.Push((root, result));
        while (pending.TryPop(out var current))
        {
            current.Context.TimeTickerChildren = new List<InternalFunctionContext>(current.Entity.Children?.Count ?? 0);
            if (current.Entity.Children == null) continue;
            foreach (var child in current.Entity.Children)
            {
                var context = createContext(child);
                current.Context.TimeTickerChildren.Add(context);
                pending.Push((child, context));
            }
        }
        return result;
    }
}
