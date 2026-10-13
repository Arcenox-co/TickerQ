using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Tests;

internal static class LicenseTestState
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    internal static TickerQLicenseStateProvider Active(ITickerClock clock = null!)
    {
        clock ??= new FixedClock();
        var now = new DateTimeOffset(clock.UtcNow.ToUniversalTime());
        var provider = new TickerQLicenseStateProvider(clock);
        provider.Publish(TickerQLicenseState.ForCertificate(
            TickerQLicenseStatus.Active,
            isEvaluation: false,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "TickerQ Tests",
            "Business",
            "Full",
            now.AddDays(-1),
            now.AddYears(1),
            daysRemaining: 365,
            schemaVersion: 4,
            algorithm: "Ed25519",
            keyId: "production-2026-01",
            anchoredMinorLine: "5.x"));
        return provider;
    }

    private sealed class FixedClock : ITickerClock
    {
        public DateTime UtcNow => FixedUtcNow;
    }
}
