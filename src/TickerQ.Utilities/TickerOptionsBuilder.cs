using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities
{
    public class TickerOptionsBuilder<TTimeTicker, TCronTicker> : ITickerOptionsSeeding
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly TickerExecutionContext _tickerExecutionContext;
        private readonly SchedulerOptionsBuilder _schedulerOptions;

        internal TickerOptionsBuilder(TickerExecutionContext tickerExecutionContext, SchedulerOptionsBuilder schedulerOptions)
        {
            _tickerExecutionContext = tickerExecutionContext;
            _schedulerOptions = schedulerOptions;
            // Store this instance in the execution context for later retrieval
            tickerExecutionContext.OptionsSeeding = this;
        }

        /// <summary>
        /// Internal flag for request GZip compression.
        /// Defaults to false (plain JSON bytes).
        /// </summary>
        internal bool RequestGZipCompressionEnabled { get; set; } = false;

        /// <summary>
        /// Controls whether code-defined cron tickers are seeded on startup.
        /// Defaults to true.
        /// </summary>
        internal bool SeedDefinedCronTickers { get; set; } = true;
        internal long ReconciliationEpoch { get; private set; }
        internal string ApplicationNamespace { get; private set; }
        internal Dictionary<string, string> LegacyDefinedCronOwnership { get; } = new(StringComparer.Ordinal);
        internal bool AdoptAllLegacyDefinedCronTickers { get; private set; }
        internal LegacyRuntimePartitionAdoption LegacyRuntimePartitionAdoption { get; private set; }
        private bool _runtimeBindingFrozen;

        private void ThrowIfRuntimeBindingFrozen()
        {
            if (_runtimeBindingFrozen)
                throw new InvalidOperationException(
                    "The TickerQ runtime partition, scheduler mode, and reconciliation epoch are already bound and cannot be mutated.");
        }

        internal void FreezeRuntimeBinding() => _runtimeBindingFrozen = true;
        
        /// <summary>
        /// Controls whether background services (job processors) should be registered.
        /// Defaults to true. Set to false to only register managers for queuing jobs.
        /// </summary>
        internal bool RegisterBackgroundServices { get; set; } = true;

        /// <summary>
        /// Seeding delegate for time tickers, executed with the application's service provider.
        /// </summary>
        internal Func<IServiceProvider, System.Threading.CancellationToken, System.Threading.Tasks.Task> TimeSeederAction { get; set; }

        /// <summary>
        /// Seeding delegate for cron tickers, executed with the application's service provider.
        /// </summary>
        internal Func<IServiceProvider, System.Threading.CancellationToken, System.Threading.Tasks.Task> CronSeederAction { get; set; }

        // Explicit interface implementation for ITickerOptionsSeeding
        bool ITickerOptionsSeeding.SeedDefinedCronTickers => SeedDefinedCronTickers;
        bool ITickerOptionsSeeding.RegisterBackgroundServices => RegisterBackgroundServices;
        long ITickerOptionsSeeding.ReconciliationEpoch => ReconciliationEpoch;
        string ITickerOptionsSeeding.ApplicationNamespace => ApplicationNamespace;
        IReadOnlyDictionary<string, string> ITickerOptionsSeeding.LegacyDefinedCronOwnership => LegacyDefinedCronOwnership;
        bool ITickerOptionsSeeding.AdoptAllLegacyDefinedCronTickers => AdoptAllLegacyDefinedCronTickers;
        LegacyRuntimePartitionAdoption ITickerOptionsSeeding.LegacyRuntimePartitionAdoption => LegacyRuntimePartitionAdoption;
        Func<IServiceProvider, System.Threading.CancellationToken, System.Threading.Tasks.Task> ITickerOptionsSeeding.TimeSeederAction => TimeSeederAction;
        Func<IServiceProvider, System.Threading.CancellationToken, System.Threading.Tasks.Task> ITickerOptionsSeeding.CronSeederAction => CronSeederAction;

        internal Action<IServiceCollection> ExternalProviderConfigServiceAction { get; set; }
        internal Action<IServiceCollection> DashboardServiceAction { get; set; }
        internal Type TickerExceptionHandlerType { get; private set; }
        
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> ConfigureScheduler(Action<SchedulerOptionsBuilder> schedulerOptionsBuilder)
        {
            schedulerOptionsBuilder?.Invoke(_schedulerOptions);
            return this;
        }

        /// <summary>
        /// Emits additional scheduler decision and recovery diagnostics through the application's
        /// normal <c>ILogger</c> pipeline. Detailed logs contain ticker identifiers and function names,
        /// but never request/result payloads. Disabled by default.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> EnableDetailedLogging(bool enabled = true)
        {
            _schedulerOptions.DetailedLoggingEnabled = enabled;
            return this;
        }

        /// <summary>
        /// Retention policy for historical terminal ticker records. Disabled by default; configure a
        /// window via <see cref="ConfigureJobRetention"/> to opt in. Never null.
        /// </summary>
        public JobRetentionOptions JobRetention { get; } = new JobRetentionOptions();

        public ExecutionLogOptions ExecutionLogs { get; } = new ExecutionLogOptions();

        /// <summary>Configure bounded log capture and optional persistence through the operational store.</summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> ConfigureExecutionLogs(Action<ExecutionLogOptions> configure)
        {
            configure?.Invoke(ExecutionLogs);
            ExecutionLogs.Validate();
            return this;
        }

        /// <summary>
        /// Enable and configure the built-in job-retention maintenance loop, which periodically deletes
        /// historical terminal ticker records older than the per-status windows you set. Cron definitions
        /// are never deleted. Retention stays disabled unless at least one window is configured.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> ConfigureJobRetention(Action<JobRetentionOptions> configure)
        {
            configure?.Invoke(JobRetention);
            JobRetention.Validate();
            return this;
        }

        /// <summary>
        /// Lower bound on the scheduler's calculated wait until the next occurrence.
        /// This is not a fixed polling cadence: later occurrences allow longer waits,
        /// and scheduler notifications can interrupt the wait. Immediate dispatch is separate.
        /// Negative values are treated as zero by the scheduling loop.
        /// </summary>
        public TimeSpan MinPollingInterval
        {
            get => _schedulerOptions.MinPollingInterval;
            set => _schedulerOptions.MinPollingInterval = value;
        }

              
        /// <summary>
        /// JsonSerializerOptions specifically for serializing/deserializing ticker requests.
        /// If not set, default JsonSerializerOptions will be used.
        /// </summary>
        internal JsonSerializerOptions RequestJsonSerializerOptions { get; set; }
        
        /// <summary>
        /// Configures a JsonSerializerContext for AOT-compatible ticker request serialization/deserialization.
        /// Use this when publishing with Native AOT or when trimming is enabled.
        /// </summary>
        /// <param name="context">The JsonSerializerContext that includes all TRequest types used in ticker functions.</param>
        /// <returns>The TickerOptionsBuilder for method chaining</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> WithJsonContext(IJsonTypeInfoResolver context)
        {
            RequestJsonSerializerOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = context
            };
            return this;
        }

        /// <summary>
        /// Configures the JSON serialization options specifically for ticker request serialization/deserialization.
        /// </summary>
        /// <param name="configure">Action to configure JsonSerializerOptions for ticker requests</param>
        /// <returns>The TickerOptionsBuilder for method chaining</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> ConfigureRequestJsonOptions(Action<JsonSerializerOptions> configure)
        {
            RequestJsonSerializerOptions ??= new JsonSerializerOptions();
            configure?.Invoke(RequestJsonSerializerOptions);
            return this;
        }

        /// <summary>
        /// Enables GZip compression for ticker request payloads.
        /// When not called, requests are stored as plain UTF-8 JSON bytes.
        /// </summary>
        /// <returns>The TickerOptionsBuilder for method chaining</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseGZipCompression()
        {
            RequestGZipCompressionEnabled = true;
            return this;
        }

        /// <summary>
        /// Enables skipping of stale cron occurrences on application startup.
        /// Idle/Queued occurrences whose execution time is further behind the current
        /// time than <paramref name="threshold"/> are marked <c>Skipped</c>, preventing
        /// duplicate executions after a restart (see #776).
        /// </summary>
        /// <param name="threshold">
        /// How far behind the current time an occurrence must be to be considered stale.
        /// Defaults to 5 seconds when not specified.
        /// </param>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SkipStaleCronOccurrencesOnStartup(TimeSpan? threshold = null)
        {
            _schedulerOptions.StaleCronOccurrenceThreshold = threshold ?? TimeSpan.FromSeconds(5);
            return this;
        }

        /// <summary>
        /// Disable automatic seeding of code-defined cron tickers on startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> IgnoreSeedDefinedCronTickers()
        {
            SeedDefinedCronTickers = false;
            return this;
        }

        /// <summary>
        /// Assigns this application's stable runtime namespace for time tickers, cron schedules, and their execution data.
        /// Required when background services are enabled. Use the same value across replicas of one application
        /// and distinct values for independent applications sharing a persistence store.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseApplicationNamespace(string applicationNamespace)
        {
            ThrowIfRuntimeBindingFrozen();
            if (string.IsNullOrWhiteSpace(applicationNamespace))
                throw new ArgumentException("A non-empty application namespace is required.", nameof(applicationNamespace));
            ApplicationNamespace = applicationNamespace.Trim();
            return this;
        }

        /// <summary>
        /// Maps one legacy, pre-namespace code-defined Cron function to the single application namespace
        /// that is allowed to adopt it in place during reconciliation.
        /// </summary>
        /// <param name="function">The exact registered function name on the legacy row.</param>
        /// <param name="ownerApplicationNamespace">
        /// The stable namespace configured by the owning application with
        /// <see cref="UseApplicationNamespace"/>.
        /// </param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentException">
        /// A value is blank or exceeds the bounded Cron seed identity contract.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The same normalized function was already mapped to a different namespace.
        /// </exception>
        /// <remarks>
        /// Legacy rows are unclaimed by default and reconciliation fails closed rather than guessing an
        /// owner. Configure the same complete mapping on every scheduler application sharing the store.
        /// Repeating an identical mapping is idempotent; a conflicting mapping is rejected deterministically.
        /// </remarks>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> MapLegacyDefinedCronOwnership(
            string function, string ownerApplicationNamespace)
        {
            if (string.IsNullOrWhiteSpace(function))
                throw new ArgumentException("A non-empty function is required.", nameof(function));
            if (string.IsNullOrWhiteSpace(ownerApplicationNamespace))
                throw new ArgumentException("A non-empty owner application namespace is required.", nameof(ownerApplicationNamespace));
            var normalizedFunction = function.Trim();
            var normalizedOwner = ownerApplicationNamespace.Trim();
            _ = CronSeedIdentity.SeedKey(normalizedOwner, normalizedFunction);
            if (LegacyDefinedCronOwnership.TryGetValue(normalizedFunction, out var existingOwner) &&
                !StringComparer.Ordinal.Equals(existingOwner, normalizedOwner))
                throw new InvalidOperationException(
                    $"Legacy Cron function '{normalizedFunction}' is already mapped to application namespace " +
                    $"'{existingOwner}' and cannot also be mapped to '{normalizedOwner}'.");
            LegacyDefinedCronOwnership[normalizedFunction] = normalizedOwner;
            return this;
        }

        /// <summary>
        /// Explicitly allows this application's namespace to claim every otherwise-unmapped legacy
        /// code-defined Cron function present in its desired manifest.
        /// </summary>
        /// <returns>This builder.</returns>
        /// <remarks>
        /// This is a sticky, idempotent, single-application upgrade opt-in. It is unsafe when independent
        /// applications share a store because legacy rows carry no trustworthy application owner. In a
        /// shared store, use <see cref="MapLegacyDefinedCronOwnership"/> on every application instead.
        /// Explicit per-function mappings take precedence over this catch-all. Without either opt-in,
        /// automatic adoption remains disabled and ambiguous legacy ownership fails closed.
        /// </remarks>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> AdoptLegacyDefinedCronTickers()
        {
            AdoptAllLegacyDefinedCronTickers = true;
            return this;
        }

        /// <summary>
        /// Selects the positive, monotonic deployment epoch used to fence startup reconciliation.
        /// Required when background services are enabled. Keep it stable for identical deployments and
        /// increase it whenever startup reconciliation inputs or semantics change.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseReconciliationEpoch(long epoch)
        {
            ThrowIfRuntimeBindingFrozen();
            if (epoch <= 0)
                throw new ArgumentOutOfRangeException(nameof(epoch), epoch,
                    "The reconciliation epoch must be positive.");
            ReconciliationEpoch = epoch;
            _schedulerOptions.ReconciliationEpoch = epoch;
            return this;
        }

        /// <summary>
        /// Explicitly authorizes this application and deployment epoch to claim all namespace-less
        /// legacy runtime state. The provider acquires a durable store-global lease before inspection.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseLegacyRuntimePartitionAdoption(
            string targetRuntimeNamespace, long reconciliationEpoch, bool legacyWritersDrained)
        {
            ThrowIfRuntimeBindingFrozen();
            var requested = new LegacyRuntimePartitionAdoption(
                new TickerQRuntimePartition(targetRuntimeNamespace), reconciliationEpoch, legacyWritersDrained);
            if (LegacyRuntimePartitionAdoption != null && LegacyRuntimePartitionAdoption != requested)
                throw new InvalidOperationException(
                    "Legacy runtime partition adoption is already configured and cannot be redirected.");
            UseApplicationNamespace(requested.TargetPartition.ApplicationNamespace);
            UseReconciliationEpoch(requested.Epoch);
            LegacyRuntimePartitionAdoption = requested;
            return this;
        }

        /// <summary>
        /// Points TickerQ at an offline license certificate. Relative paths are resolved from the host
        /// content root. When omitted, TickerQ looks for <c>tickerq.tqlicense</c> in the content root.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseLicense(string certificatePath)
        {
            _tickerExecutionContext.LicenseCertificatePath = certificatePath;
            return this;
        }
        
        /// <summary>
        /// Disables background services registration. 
        /// Use this when you only want to queue jobs without processing them in this application.
        /// Only the managers (ITimeTickerManager, ICronTickerManager) will be available for queuing jobs.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> DisableBackgroundServices()
        {
            ThrowIfRuntimeBindingFrozen();
            RegisterBackgroundServices = false;
            return this;
        }

        /// <summary>
        /// Creates an ActivitySource named "TickerQ" with activity tracing for TickerQ jobs.
        /// Also includes standard logging through ILogger.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> EnableActivitySource()
        {
            ExternalProviderConfigServiceAction += services =>
            {
                services.AddSingleton<ITickerQInstrumentation, ActivitySourceInstrumentation>();
            };
            return this;
        }

        /// <summary>
        /// Configure a custom seeder for time tickers, executed on application startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ITimeTickerManager<TTimeTicker>, System.Threading.Tasks.Task> timeSeeder)
        {
            if (timeSeeder == null) return this;
            return UseTickerSeeder((manager, _) => timeSeeder(manager));
        }

        /// <summary>
        /// Configure a cancellation-aware custom seeder for time tickers. The token represents host startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ITimeTickerManager<TTimeTicker>, System.Threading.CancellationToken, System.Threading.Tasks.Task> timeSeeder)
        {
            if (timeSeeder == null) return this;

            TimeSeederAction = async (sp, cancellationToken) =>
            {
                var manager = sp.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
                await timeSeeder(manager, cancellationToken).ConfigureAwait(false);
            };

            return this;
        }

        /// <summary>
        /// Configure a custom seeder for cron tickers, executed on application startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ICronTickerManager<TCronTicker>, System.Threading.Tasks.Task> cronSeeder)
        {
            if (cronSeeder == null) return this;
            return UseTickerSeeder((manager, _) => cronSeeder(manager));
        }

        /// <summary>
        /// Configure a cancellation-aware custom seeder for cron tickers. The token represents host startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ICronTickerManager<TCronTicker>, System.Threading.CancellationToken, System.Threading.Tasks.Task> cronSeeder)
        {
            if (cronSeeder == null) return this;

            CronSeederAction = async (sp, cancellationToken) =>
            {
                var manager = sp.GetRequiredService<ICronTickerManager<TCronTicker>>();
                await cronSeeder(manager, cancellationToken).ConfigureAwait(false);
            };

            return this;
        }

        /// <summary>
        /// Configure custom seeders for both time and cron tickers, executed on application startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ITimeTickerManager<TTimeTicker>, System.Threading.Tasks.Task> timeSeeder,
            Func<ICronTickerManager<TCronTicker>, System.Threading.Tasks.Task> cronSeeder)
        {
            UseTickerSeeder(timeSeeder);
            UseTickerSeeder(cronSeeder);
            return this;
        }

        /// <summary>
        /// Configure cancellation-aware custom seeders for both time and cron tickers.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ITimeTickerManager<TTimeTicker>, System.Threading.CancellationToken, System.Threading.Tasks.Task> timeSeeder,
            Func<ICronTickerManager<TCronTicker>, System.Threading.CancellationToken, System.Threading.Tasks.Task> cronSeeder)
        {
            UseTickerSeeder(timeSeeder);
            UseTickerSeeder(cronSeeder);
            return this;
        }
        
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SetExceptionHandler<THandler>() where THandler : ITickerExceptionHandler
        {
            TickerExceptionHandlerType = typeof(THandler);
            return this;
        }

        internal void UseExternalProviderApplication(Action<IServiceProvider> action)
            => _tickerExecutionContext.ExternalProviderApplicationAction = action;
        
        internal void UseDashboardApplication(Action<object> action)
            => _tickerExecutionContext.DashboardApplicationAction = action;
    }

    public class SchedulerOptionsBuilder
    {
        private readonly string _executionOwnerNonce = Guid.NewGuid().ToString("N");
        private bool _runtimeActivationScopeBound;
        private long _reconciliationEpoch = 1;

        /// <summary>
        /// The immutable application scope this scheduler instance admits for runtime work. It is bound
        /// from TickerQ registration options before any persistence provider can be resolved; protocol
        /// calls for other scopes cannot redirect it.
        /// </summary>
        internal ReconciliationActivationScope RuntimeActivationScope { get; private set; }
        internal TickerQRuntimePartition RuntimePartition { get; private set; }
        internal long RuntimeActivationEpoch { get; private set; }
        internal bool RuntimeSchedulerEnabled { get; private set; }
        internal bool HasRuntimeActivationScopeBinding => _runtimeActivationScopeBound;

        /// <summary>
        /// The configured application namespace that isolates this scheduler's jobs and cluster state.
        /// Returns <see langword="null"/> only for the namespace-less legacy queue-only mode.
        /// </summary>
        public string ApplicationNamespace => RuntimePartition?.ApplicationNamespace;

        internal void BindRuntimeActivationScope(string applicationNamespace, long epoch, bool schedulerEnabled)
        {
            var partition = TickerQRuntimePartition.Bind(applicationNamespace, schedulerEnabled);
            var scope = partition.IsLegacyGlobal ? null : new ReconciliationActivationScope(partition.ApplicationNamespace);
            if (_runtimeActivationScopeBound)
            {
                if (RuntimeSchedulerEnabled == schedulerEnabled
                    && RuntimeActivationEpoch == epoch
                    && Equals(RuntimePartition, partition))
                    return;
                throw new InvalidOperationException(
                    "The TickerQ runtime activation scope is already bound and cannot be redirected.");
            }

            RuntimeActivationScope = scope;
            RuntimePartition = partition;
            RuntimeActivationEpoch = epoch;
            RuntimeSchedulerEnabled = schedulerEnabled;
            _reconciliationEpoch = epoch;
            _runtimeActivationScopeBound = true;
        }

        /// <summary>Human-readable logical node label used by dashboards, metrics, and heartbeat providers.</summary>
        public string NodeIdentifier { get; set; } = Environment.MachineName;

        /// <summary>
        /// Enables additional operational diagnostics for scheduler acquisition, queue publication,
        /// recovery, and restart decisions. Prefer <c>EnableDetailedLogging()</c> on the main options builder.
        /// Request and result payloads are never included.
        /// </summary>
        public bool DetailedLoggingEnabled { get; set; }

        /// <summary>Optional friendly node name shown in the dashboard. Defaults to NodeIdentifier.</summary>
        public string NodeDisplayName { get; set; }

        /// <summary>
        /// Optional absolute HTTP(S) dashboard URL advertised for human navigation from the Nodes page.
        /// It is never used as a control-plane endpoint; cross-node commands travel through the
        /// operational store.
        /// </summary>
        public string AdvertisedDashboardUrl { get; set; }

        /// <summary>
        /// Process-instance-unique persistence owner used for LockHolder fencing. Two scheduler
        /// processes may intentionally share NodeIdentifier, but must never share row ownership.
        /// </summary>
        public string ExecutionOwnerId => $"{NodeIdentifier}:{Environment.ProcessId}:{_executionOwnerNonce}";
        /// <summary>
        /// The exact reconciliation epoch this scheduler binary can execute against. Persistence providers
        /// use it to fail closed when a shared store is activating or has published a different epoch.
        /// </summary>
        public long ReconciliationEpoch
        {
            get => _reconciliationEpoch;
            internal set
            {
                if (_runtimeActivationScopeBound && value != RuntimeActivationEpoch)
                    throw new InvalidOperationException(
                        "The TickerQ runtime activation epoch is already bound and cannot be redirected.");
                _reconciliationEpoch = value;
            }
        }
        /// <summary>Maximum concurrent function attempts on this host, including individual chain steps.</summary>
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
        /// <summary>Maximum time-ticker roots loaded in one scheduled or immediate acquisition.</summary>
        public int AcquisitionBatchSize { get; set; } = 100;
        /// <summary>Maximum handler attempts waiting in the built-in execution queue.</summary>
        public int MaxQueuedExecutions { get; set; } = 1024;
        /// <summary>Maximum admitted root workflows, including chains and jobs waiting to retry.</summary>
        public int MaxActiveWorkflows { get; set; } = 128;
        /// <summary>Maximum live step tasks per chain. Pending steps remain lightweight graph nodes.</summary>
        public int MaxConcurrentChainSteps { get; set; } = 16;
        public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>Maximum overdue roots of each ticker type acquired per built-in provider scan.</summary>
        public int FallbackBatchSize { get; set; } = 100;
        /// <summary>
        /// Lower bound on the scheduler's calculated wait until the next occurrence.
        /// This is not a fixed polling cadence: later occurrences allow longer waits,
        /// and scheduler notifications can interrupt the wait. Immediate dispatch is separate.
        /// Negative values are treated as zero by the scheduling loop.
        /// </summary>
        public TimeSpan MinPollingInterval { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>
        /// How far behind the current time an Idle/Queued cron occurrence must be
        /// before it is marked <c>Skipped</c> on startup. Occurrences whose
        /// <c>ExecutionTime</c> is closer to the current time than this threshold
        /// are left for normal scheduling to pick up.
        /// <para>
        /// Disabled by default (<see cref="TimeSpan.Zero"/>). Enable via
        /// <c>SkipStaleCronOccurrencesOnStartup()</c> on the options builder.
        /// </para>
        /// </summary>
        public TimeSpan StaleCronOccurrenceThreshold { get; set; } = TimeSpan.Zero;
        public TimeZoneInfo SchedulerTimeZone = TimeZoneInfo.Local;

        private TimeSpan _definedCronRetirementGracePeriod = TimeSpan.FromHours(24);

        /// <summary>
        /// How long a previously-seeded code-defined cron whose definition has disappeared from the
        /// desired seed manifest is kept enabled before the reconciler retires it (two-phase,
        /// non-destructive retirement). On the first reconcile that observes the seed as no longer
        /// desired, its <c>RetirementRequestedAt</c> is stamped and it stays enabled; only on a later
        /// reconcile once this grace window has elapsed is it disabled and marked <c>RetiredAt</c>. The
        /// row and all of its occurrences/results are always preserved — retention, not reconciliation,
        /// removes history.
        /// <para>
        /// Defaults to 24 hours: comfortably longer than a typical rolling deploy, so a node that
        /// temporarily rolls back to an older build (which does not project the seed) cannot cause the
        /// schedule to be retired mid-rollout. Set to <see cref="TimeSpan.Zero"/> for immediate
        /// retirement in controlled single-node deployments. A blocked required-contract seed is retired
        /// immediately regardless of this grace, because continuing to schedule an unsatisfiable request
        /// is unsafe.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public TimeSpan DefinedCronRetirementGracePeriod
        {
            get => _definedCronRetirementGracePeriod;
            set
            {
                if (value < TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "DefinedCronRetirementGracePeriod cannot be negative.");
                _definedCronRetirementGracePeriod = value;
            }
        }

        /// <summary>
        /// Runtime stale-job recovery: while a ticker executes, the owning node
        /// renews a lease on the row; if the node dies (crash, OOM, eviction) the
        /// lease expires and a watchdog applies the ticker's <c>OnStale</c> action
        /// (Restart by default, Cancel opt-in per job). Disable to keep the
        /// previous behavior where a dead node leaves rows InProgress forever.
        /// </summary>
        public bool StaleJobRecoveryEnabled { get; set; } = true;

        /// <summary>
        /// How long a lease lasts from each renewal. Must be comfortably larger
        /// than <see cref="LeaseRenewalInterval"/> (3× or more) so a transient DB
        /// hiccup or GC pause doesn't get a live job treated as stale.
        /// </summary>
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(45);

        /// <summary>How often the running node renews leases for its active jobs.</summary>
        public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Maximum age of the short persisted Queued handoff lock before recovery resets it to Idle.
        /// Normal accepted work transitions to InProgress immediately; this protects the crash window
        /// between queue acquisition and that transition without releasing a live sibling on startup.
        /// </summary>
        public TimeSpan QueuedLockTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Poison-job guard: after this many stale Restarts the ticker is Cancelled
        /// instead, so a job that kills its host can't crash-loop the cluster.
        /// </summary>
        public int MaxStaleRestarts { get; set; } = 3;

        /// <summary>
        /// Global per-attempt execution timeout applied to every ticker that doesn't
        /// set its own <c>TimeoutSeconds</c>. Null (default) means no timeout.
        /// Exceeding it cancels the execution (Cancelled with a timeout reason);
        /// functions that don't honor their CancellationToken are abandoned and
        /// logged — their thread keeps running until it finishes on its own.
        /// </summary>
        public TimeSpan? DefaultExecutionTimeout { get; set; }

        /// <summary>
        /// Grace after cooperative timeout cancellation before a still-running delegate is logged
        /// as timeout-pending. The delegate remains tracked, leased, and scoped until it actually exits.
        /// In-process delegates cannot be terminated safely.
        /// </summary>
        public TimeSpan TimeoutGracePeriod { get; set; } =
            TimeSpan.FromSeconds(DefinedCronExecutionLimits.DefaultTimeoutGraceSeconds);

        /// <summary>
        /// On graceful shutdown, how long to wait for in-flight ticker executions
        /// to finish before letting the host exit. Zero disables draining (jobs are
        /// abandoned and later healed by stale-job recovery). Bounded additionally
        /// by the host's own shutdown timeout (<c>HostOptions.ShutdownTimeout</c>).
        /// </summary>
        public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

        internal void Validate()
        {
            if (!string.IsNullOrWhiteSpace(AdvertisedDashboardUrl)
                && (!Uri.TryCreate(AdvertisedDashboardUrl, UriKind.Absolute, out var advertised)
                    || advertised.Scheme is not ("http" or "https") || advertised.UserInfo.Length > 0))
                throw new ArgumentException("AdvertisedDashboardUrl must be an absolute HTTP or HTTPS URL without credentials.", nameof(AdvertisedDashboardUrl));
            if (MaxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), "At least one worker is required.");
            ValidateCapacity(AcquisitionBatchSize, nameof(AcquisitionBatchSize), 10000);
            ValidateCapacity(MaxQueuedExecutions, nameof(MaxQueuedExecutions), 100000);
            ValidateCapacity(MaxActiveWorkflows, nameof(MaxActiveWorkflows), 10000);
            ValidateCapacity(MaxConcurrentChainSteps, nameof(MaxConcurrentChainSteps), 1000);
            if (FallbackBatchSize < 1 || FallbackBatchSize > 10000)
                throw new ArgumentOutOfRangeException(nameof(FallbackBatchSize), "Use a batch size between 1 and 10000.");
            ValidateTimer(FallbackIntervalChecker, nameof(FallbackIntervalChecker));
            ValidateTimer(LeaseRenewalInterval, nameof(LeaseRenewalInterval));
            ValidateTimer(LeaseDuration, nameof(LeaseDuration));
            ValidateTimer(QueuedLockTimeout, nameof(QueuedLockTimeout));
            ValidateTimer(ShutdownDrainTimeout, nameof(ShutdownDrainTimeout), allowZero: true);
            // Preserve the documented negative-as-zero scheduler wait behavior.
            ValidateTimer(MinPollingInterval < TimeSpan.Zero ? TimeSpan.Zero : MinPollingInterval,
                nameof(MinPollingInterval), allowZero: true);
            if (LeaseDuration.Ticks / 3 < LeaseRenewalInterval.Ticks)
                throw new ArgumentException("LeaseDuration must be at least three times LeaseRenewalInterval.", nameof(LeaseDuration));
            if (MaxStaleRestarts < 0) throw new ArgumentOutOfRangeException(nameof(MaxStaleRestarts));
        }

        private static void ValidateTimer(TimeSpan value, string name, bool allowZero = false)
        {
            if (value < (allowZero ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1))
                || value.TotalMilliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(name, "Duration must fit a finite .NET timer; positive intervals must be at least 1 ms.");
        }

        private static void ValidateCapacity(int value, string name, int maximum)
        {
            if (value < 1 || value > maximum)
                throw new ArgumentOutOfRangeException(name, $"Use a capacity between 1 and {maximum}.");
        }
    }
}
