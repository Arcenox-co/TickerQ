using System.Threading;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities.Licensing
{
    /// <summary>
    /// Process-wide holder for the single, validated license state. Registered as a DI singleton so the Core
    /// enforcement path and the Dashboard resolve the exact same instance. The runtime license hosted service
    /// validates once at startup and calls <see cref="Publish"/>; every other consumer only reads
    /// <see cref="Current"/>. Reads default to a fail-closed <see cref="TickerQLicenseState.NotValidated"/>
    /// until that first publication.
    /// </summary>
    public sealed class TickerQLicenseStateProvider
    {
        private readonly ITickerClock _clock;
        private TickerQLicenseState _current = TickerQLicenseState.NotValidated;

        public TickerQLicenseStateProvider(ITickerClock clock)
        {
            _clock = clock;
        }

        /// <summary>The current, self-consistent license snapshot. Never null.</summary>
        public TickerQLicenseState Current
        {
            get
            {
                var current = Volatile.Read(ref _current);
                var observedAt = new System.DateTimeOffset(_clock.UtcNow, System.TimeSpan.Zero);
                var refreshed = current.RefreshTemporalStatus(observedAt);
                if (!ReferenceEquals(current, refreshed))
                    Interlocked.CompareExchange(ref _current, refreshed, current);
                return Volatile.Read(ref _current);
            }
        }

        /// <summary>Convenience shortcut for the enforcement path.</summary>
        public bool ExecutionAllowed => Current.ExecutionAllowed;

        /// <summary>Publishes the validated state as a single atomic unit. Called once at startup.</summary>
        public void Publish(TickerQLicenseState state)
        {
            if (state != null)
                Volatile.Write(ref _current, state);
        }
    }
}
