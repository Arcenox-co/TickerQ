using System;
using System.Threading;

namespace TickerQ.Utilities.Models;

/// <summary>
/// Internal async-flow capability used only while the initializer invokes configured startup seeders.
/// Consumer assemblies cannot construct or install the capability; providers can only inspect whether
/// the current flow matches their immutable construction-bound activation scope and epoch.
/// </summary>
internal static class StartupSeederAdmissionContext
{
    private sealed record Admission(string ScopeKey, long Epoch);

    private static readonly AsyncLocal<Admission> Current = new();

    internal static bool Matches(string scopeKey, long epoch)
    {
        var admission = Current.Value;
        return admission != null
               && epoch > 0
               && string.Equals(admission.ScopeKey, scopeKey, StringComparison.Ordinal)
               && admission.Epoch == epoch;
    }

    internal static IDisposable Enter(string scopeKey, long epoch)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
            throw new ArgumentException("A startup-seeder activation scope key is required.", nameof(scopeKey));
        if (epoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(epoch), epoch,
                "A startup-seeder activation epoch must be positive.");

        var prior = Current.Value;
        Current.Value = new Admission(scopeKey, epoch);
        return new Scope(prior);
    }

    private sealed class Scope(Admission prior) : IDisposable
    {
        private Admission _prior = prior;

        public void Dispose()
        {
            Current.Value = Interlocked.Exchange(ref _prior, null);
        }
    }
}
