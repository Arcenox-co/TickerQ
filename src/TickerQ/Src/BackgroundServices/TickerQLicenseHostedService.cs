using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Licensing;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Licensing;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Validates the offline license certificate exactly once at startup and publishes an immutable
/// <see cref="TickerQLicenseState"/> that the rest of TickerQ (and the Dashboard) enforce against.
///
/// Registered before <see cref="TickerQInitializerHostedService"/> and every operational hosted service so
/// the license verdict is known before any discovery, seeding, scheduling, or execution can occur. It is
/// deliberately side-effect-free: it never touches managers, providers, or the store, and a Missing, Invalid,
/// or Expired certificate never throws — the host and Dashboard still start so the state can be diagnosed.
/// A single clear warning is logged for a state that blocks, restricts, or has expired; the operational loops
/// stay quiet afterwards to avoid repeating it.
/// </summary>
internal sealed class TickerQLicenseHostedService : IHostedService
{
    private readonly TickerExecutionContext _executionContext;
    private readonly TickerQLicenseStateProvider _licenseState;
    private readonly IHostEnvironment _environment;
    private readonly ITickerClock _clock;
    private readonly ILogger<TickerQLicenseHostedService> _logger;

    public TickerQLicenseHostedService(
        TickerExecutionContext executionContext,
        TickerQLicenseStateProvider licenseState,
        IHostEnvironment environment,
        ITickerClock clock,
        ILogger<TickerQLicenseHostedService> logger = null)
    {
        _executionContext = executionContext;
        _licenseState = licenseState;
        _environment = environment;
        _clock = clock;
        _logger = logger ?? NullLogger<TickerQLicenseHostedService>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var now = new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero);
        var contentRoot = _environment?.ContentRootPath;

        var state = LicenseCertificateReader.Read(_executionContext?.LicenseCertificatePath, contentRoot, now);
        _licenseState.Publish(state);

        // Exactly one line at startup. Anything that blocks or restricts execution warns; an in-force
        // paid/Community certificate is a single informational line.
        if (!state.ExecutionAllowed || state.IsEvaluation || state.Status == TickerQLicenseStatus.Expiring || state.Status == TickerQLicenseStatus.Expired)
            _logger.LogWarning("TickerQ license: {Message}", state.Message);
        else
            _logger.LogInformation("TickerQ license: {Message}", state.Message);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
