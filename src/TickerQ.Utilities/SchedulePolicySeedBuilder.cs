using System;
using System.Collections.Generic;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Declares the schedule policies this application owns. When startup reconciles, each policy is
    /// created or updated by name, in declaration order, before seeded crons and time tickers are
    /// validated, so a seed may use a policy declared here. Policies are never deleted by seeding.
    /// </summary>
    public sealed class SchedulePolicySeedBuilder
    {
        private readonly List<SchedulePolicy> _policies = new();
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        internal IReadOnlyList<SchedulePolicy> Policies => _policies;

        /// <summary>Adds a policy, for example <c>SchedulePolicy.Create("Business hours").AllowBetween(...)</c>.</summary>
        public SchedulePolicySeedBuilder Add(SchedulePolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            var name = SchedulePolicyNames.Normalize(policy.Name);
            if (name.Length == 0)
                throw new ArgumentException("A seeded schedule policy needs a name.", nameof(policy));
            if (!_names.Add(name))
                throw new InvalidOperationException($"A schedule policy named '{policy.Name}' is already seeded.");
            _policies.Add(policy.Copy());
            return this;
        }
    }
}
