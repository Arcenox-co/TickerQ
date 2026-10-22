using System;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Dashboard.Assistant;

/// <summary>
/// Controls the bounded evidence collected by the dashboard's AI run investigator.
/// Request payload values are deliberately excluded; the investigator reads execution
/// metadata, the stored exception, retained logs, and workflow state.
/// </summary>
public sealed class RunInvestigatorOptionsBuilder
{
    internal int MaxLogLines { get; private set; } = 50;
    internal int MaxChainSteps { get; private set; } = 50;
    internal int MaxSourceLines { get; private set; } = 80;
    internal int MaxSourceCharacters { get; private set; } = 12_000;
    internal Func<IServiceProvider, IExecutionSourceContextProvider>? SourceProviderFactory { get; private set; }
    internal Func<string, string> EvidenceRedactor { get; private set; } = static value => value;
    internal AutomaticInvestigationOptionsBuilder Automatic { get; } = new();

    /// <summary>Limit retained log lines included in one investigation (default 50, maximum 100).</summary>
    public RunInvestigatorOptionsBuilder WithMaxLogLines(int maximum)
    {
        if (maximum is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Log lines must be between 1 and 100.");
        MaxLogLines = maximum;
        return this;
    }

    /// <summary>Limit workflow steps included as surrounding context (default 50, maximum 100).</summary>
    public RunInvestigatorOptionsBuilder WithMaxChainSteps(int maximum)
    {
        if (maximum is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Chain steps must be between 1 and 100.");
        MaxChainSteps = maximum;
        return this;
    }

    /// <summary>
    /// Resolve a small source excerpt on the server. The provider can read a local checkout,
    /// SourceLink artifact, or private repository with host-owned credentials. Credentials and
    /// repository access are never exposed to the browser or persisted by TickerQ. The application
    /// owns the returned provider's lifetime; resolving a DI-managed singleton is recommended.
    /// </summary>
    public RunInvestigatorOptionsBuilder UseSourceContextProvider(
        Func<IServiceProvider, IExecutionSourceContextProvider> factory)
    {
        SourceProviderFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Use an already-constructed source-context provider.</summary>
    public RunInvestigatorOptionsBuilder UseSourceContextProvider(IExecutionSourceContextProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        SourceProviderFactory = _ => provider;
        return this;
    }

    /// <summary>
    /// Read bounded source excerpts from an explicitly allow-listed local checkout. This is useful
    /// for development and for deployments that deliberately ship their matching source revision.
    /// Function mappings should be preferred when multiple projects contain the same filename.
    /// </summary>
    public RunInvestigatorOptionsBuilder UseLocalSourceContext(
        string sourceRoot,
        Action<LocalSourceContextOptionsBuilder> configure)
        => UseSourceContextProvider(new LocalCheckoutSourceContextProvider(sourceRoot, configure));

    /// <summary>Bound source excerpts returned by the provider (defaults: 80 lines / 12,000 characters).</summary>
    public RunInvestigatorOptionsBuilder WithSourceContextLimits(int maximumLines, int maximumCharacters)
    {
        if (maximumLines is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(maximumLines), "Source lines must be between 1 and 200.");
        if (maximumCharacters is < 256 or > 32_000)
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters), "Source characters must be between 256 and 32,000.");
        MaxSourceLines = maximumLines;
        MaxSourceCharacters = maximumCharacters;
        return this;
    }

    /// <summary>
    /// Redact application logs, exception text, and source excerpts before they are sent to
    /// the configured AI provider. If the callback fails, that evidence item is omitted.
    /// </summary>
    public RunInvestigatorOptionsBuilder RedactEvidenceWith(Func<string, string> redactor)
    {
        EvidenceRedactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        return this;
    }

    /// <summary>
    /// Opt in to background investigation of terminal failures. This can call the configured AI
    /// provider without an interactive user request, so it is disabled by default and requires a
    /// durable <c>IAssistantInvestigationStore</c> and
    /// <c>IAssistantInvestigationCoordinator</c> (the EF <c>AddAssistantHistory()</c> option
    /// provides both). Evidence leases and the hourly model-call budget are shared across nodes.
    /// </summary>
    public RunInvestigatorOptionsBuilder EnableAutomaticInvestigations(
        Action<AutomaticInvestigationOptionsBuilder>? configure = null)
    {
        Automatic.Enabled = true;
        configure?.Invoke(Automatic);
        return this;
    }
}

public sealed class AutomaticInvestigationOptionsBuilder
{
    internal bool Enabled { get; set; }
    internal TimeSpan PollInterval { get; private set; } = TimeSpan.FromSeconds(30);
    internal TimeSpan Lookback { get; private set; } = TimeSpan.FromMinutes(15);
    internal int BatchSize { get; private set; } = 10;
    internal int MaxInvestigationsPerHour { get; private set; } = 30;
    internal TimeSpan CoordinationLease { get; private set; } = TimeSpan.FromMinutes(5);
    internal bool IncludeCancelled { get; private set; }
    internal bool DetectRecoveries { get; private set; } = true;

    public AutomaticInvestigationOptionsBuilder Every(TimeSpan interval)
    {
        if (interval < TimeSpan.FromSeconds(10) || interval > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be between 10 seconds and 10 minutes.");
        PollInterval = interval;
        return this;
    }

    public AutomaticInvestigationOptionsBuilder WithLookback(TimeSpan lookback)
    {
        if (lookback < TimeSpan.FromMinutes(1) || lookback > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(lookback), "Lookback must be between one minute and one day.");
        Lookback = lookback;
        return this;
    }

    public AutomaticInvestigationOptionsBuilder WithBatchSize(int batchSize)
    {
        if (batchSize is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 50.");
        BatchSize = batchSize;
        return this;
    }

    public AutomaticInvestigationOptionsBuilder WithHourlyLimit(int maximum)
    {
        if (maximum is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Hourly limit must be between 1 and 500.");
        MaxInvestigationsPerHour = maximum;
        return this;
    }

    /// <summary>
    /// Set the renewable cluster lease used while one node investigates a failure pattern
    /// (default five minutes). A crashed node's lease becomes recoverable after this interval.
    /// </summary>
    public AutomaticInvestigationOptionsBuilder WithCoordinationLease(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(duration),
                "Coordination lease must be between one and 30 minutes.");
        CoordinationLease = duration;
        return this;
    }

    public AutomaticInvestigationOptionsBuilder IncludeCancellations(bool include = true)
    {
        IncludeCancelled = include;
        return this;
    }

    public AutomaticInvestigationOptionsBuilder DetectSuccessfulRecovery(bool enabled = true)
    {
        DetectRecoveries = enabled;
        return this;
    }
}

/// <summary>
/// Optional application-owned adapter for resolving source around an exception frame.
/// Implementations should use an immutable revision when reading a remote repository.
/// </summary>
public interface IExecutionSourceContextProvider
{
    Task<ExecutionSourceContext?> GetSourceContextAsync(
        ExecutionSourceContextRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ExecutionSourceContextRequest
{
    public Guid ExecutionId { get; init; }
    public string FunctionName { get; init; } = "";
    public string ExceptionType { get; init; } = "";
    public string Method { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public int? SourceLine { get; init; }
}

public sealed class ExecutionSourceContext
{
    /// <summary>Friendly repository label; do not include credentials or credential-bearing URLs.</summary>
    public string Repository { get; init; } = "";

    /// <summary>Immutable commit/revision used for the lookup.</summary>
    public string Revision { get; init; } = "";

    /// <summary>Repository-relative source path.</summary>
    public string Path { get; init; } = "";

    public int StartLine { get; init; }
    public string Code { get; init; } = "";
}
