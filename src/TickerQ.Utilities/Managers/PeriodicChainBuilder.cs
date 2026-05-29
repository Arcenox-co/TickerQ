using System;
using System.Collections.Generic;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Managers
{
    /// <summary>
    /// Fluent builder for a periodic ticker's chain template (<see cref="PeriodicChainStep"/>[]).
    /// Naming mirrors <see cref="FluentChainTickerBuilder{TTimeTicker}"/> / <see cref="ChildBuilder{TTimeTicker}"/>
    /// for consistency: <c>WithChild</c> plus <c>SetFunction</c>/<c>SetRunCondition</c>/<c>SetRetries</c>/<c>SetRequest</c>.
    /// </summary>
    /// <remarks>
    /// The chain ROOT is the periodic ticker itself (its Function/Request/Retries); this builder describes the
    /// root's descendants. Multiple <c>WithChild</c> calls at the same level are parallel siblings (fan-out, run via
    /// Task.WhenAll). For a strictly sequential spine (e.g. a shared COM port), nest each next step via the
    /// <c>descendants</c> action so there is exactly one child per level.
    /// <para>
    /// Breadth/depth are bounded by <see cref="TickerChainConfig"/> (configurable via <c>SetChainLimits(...)</c>).
    /// The execution engine itself supports arbitrary depth/branching.
    /// </para>
    /// </remarks>
    public sealed class PeriodicChainBuilder
    {
        private readonly List<PeriodicChainStep> _steps = new List<PeriodicChainStep>();
        private readonly int _depth; // level of steps added by THIS builder instance (root children = 1)

        private PeriodicChainBuilder(int depth) => _depth = depth;

        /// <summary>
        /// Starts a new chain template. The first <see cref="WithChild(System.Action{ChildBuilder{TimeTickerEntity}}, System.Action{PeriodicChainBuilder})"/>
        /// calls add the periodic root's direct children (level 1).
        /// </summary>
        public static PeriodicChainBuilder Create() => new PeriodicChainBuilder(1);

        /// <summary>
        /// Adds a child step at the current level using the same <see cref="ChildBuilder{TTimeTicker}"/> vocabulary
        /// as the TimeTicker chain builder. Optionally configures its descendants via <paramref name="descendants"/>
        /// for arbitrary depth (one child per level = sequential spine).
        /// </summary>
        public PeriodicChainBuilder WithChild(
            Action<ChildBuilder<TimeTickerEntity>> configure,
            Action<PeriodicChainBuilder> descendants = null)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            EnsureWithinLimits();

            // Reuse the TimeTicker ChildBuilder to populate a throwaway entity, then project onto a step.
            var temp = new TimeTickerEntity { Children = new List<TimeTickerEntity>() };
            configure(new ChildBuilder<TimeTickerEntity>(temp));

            var step = new PeriodicChainStep
            {
                Function = temp.Function,
                RunCondition = temp.RunCondition ?? RunCondition.OnSuccess,
                Retries = temp.Retries,
                RetryIntervals = temp.RetryIntervals,
                Request = temp.Request
            };
            _steps.Add(step);

            ApplyDescendants(step, descendants);
            return this;
        }

        private void ApplyDescendants(PeriodicChainStep step, Action<PeriodicChainBuilder> descendants)
        {
            if (descendants == null) return;

            var sub = new PeriodicChainBuilder(_depth + 1);
            descendants(sub);
            foreach (var s in sub._steps)
                step.Children.Add(s);
        }

        private void EnsureWithinLimits()
        {
            if (_depth > TickerChainConfig.MaxDepth)
                throw new InvalidOperationException(
                    $"Chain depth {_depth} exceeds the configured maximum ({TickerChainConfig.MaxDepth}). " +
                    "Increase it via SetChainLimits(maxChildrenPerNode, maxDepth).");

            if (_steps.Count >= TickerChainConfig.MaxChildrenPerNode)
                throw new InvalidOperationException(
                    $"Number of children at this level exceeds the configured maximum ({TickerChainConfig.MaxChildrenPerNode}). " +
                    "Increase it via SetChainLimits(maxChildrenPerNode, maxDepth).");
        }

        /// <summary>
        /// Builds the template array (the periodic root's direct children).
        /// </summary>
        public PeriodicChainStep[] Build() => _steps.ToArray();

        /// <summary>
        /// Implicit conversion for direct assignment to <see cref="PeriodicTickerEntity.ChainTemplate"/>.
        /// </summary>
        public static implicit operator PeriodicChainStep[](PeriodicChainBuilder builder) => builder.Build();
    }
}

