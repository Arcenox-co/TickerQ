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
        var rootChildren = root.GetChildrenForRead();
        PrepareChildren(result, rootChildren?.Count ?? 0);
        if (rootChildren is not { Count: > 0 })
            return result;

        var pending = new Stack<(TimeTickerEntity Entity, InternalFunctionContext Context)>();
        pending.Push((root, result));
        while (pending.TryPop(out var current))
        {
            var children = current.Entity.GetChildrenForRead();
            if (children == null) continue;
            foreach (var child in children)
            {
                var context = createContext(child);
                var descendants = child.GetChildrenForRead();
                PrepareChildren(context, descendants?.Count ?? 0);
                current.Context.TimeTickerChildren.Add(context);
                if (descendants is { Count: > 0 })
                    pending.Push((child, context));
            }
        }
        return result;
    }

    private static void PrepareChildren(InternalFunctionContext context, int count)
    {
        // Context factories provide an owned list. Reuse it instead of allocating
        // a replacement for every node, including the common leaf-only root.
        var children = context.TimeTickerChildren ??= [];
        children.Clear();
        children.EnsureCapacity(count);
    }
}
