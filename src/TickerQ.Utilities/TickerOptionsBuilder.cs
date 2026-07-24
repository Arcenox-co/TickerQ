using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

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
        
        /// <summary>
        /// Controls whether background services (job processors) should be registered.
        /// Defaults to true. Set to false to only register managers for queuing jobs.
        /// </summary>
        internal bool RegisterBackgroundServices { get; set; } = true;

        /// <summary>
        /// Seeding delegate for time tickers, executed with the application's service provider.
        /// </summary>
        internal Func<IServiceProvider, System.Threading.Tasks.Task> TimeSeederAction { get; set; }

        /// <summary>
        /// Seeding delegate for cron tickers, executed with the application's service provider.
        /// </summary>
        internal Func<IServiceProvider, System.Threading.Tasks.Task> CronSeederAction { get; set; }

        // Explicit interface implementation for ITickerOptionsSeeding
        bool ITickerOptionsSeeding.SeedDefinedCronTickers => SeedDefinedCronTickers;
        Func<IServiceProvider, System.Threading.Tasks.Task> ITickerOptionsSeeding.TimeSeederAction => TimeSeederAction;
        Func<IServiceProvider, System.Threading.Tasks.Task> ITickerOptionsSeeding.CronSeederAction => CronSeederAction;

        internal Action<IServiceCollection> ExternalProviderConfigServiceAction { get; set; }
        internal Action<IServiceCollection> DashboardServiceAction { get; set; }
        internal Type TickerExceptionHandlerType { get; private set; }
        
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> ConfigureScheduler(Action<SchedulerOptionsBuilder> schedulerOptionsBuilder)
        {
            schedulerOptionsBuilder?.Invoke(_schedulerOptions);
            return this;
        }

        /// <summary>
        /// Gets or sets the minimum interval between database polls.
        /// Prevents tight loops when tasks are due or when the database is empty.
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
        /// Disables background services registration. 
        /// Use this when you only want to queue jobs without processing them in this application.
        /// Only the managers (ITimeTickerManager, ICronTickerManager) will be available for queuing jobs.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> DisableBackgroundServices()
        {
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

            TimeSeederAction = async sp =>
            {
                var manager = sp.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
                await timeSeeder(manager).ConfigureAwait(false);
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

            CronSeederAction = async sp =>
            {
                var manager = sp.GetRequiredService<ICronTickerManager<TCronTicker>>();
                await cronSeeder(manager).ConfigureAwait(false);
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
        
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SetExceptionHandler<THandler>() where THandler : ITickerExceptionHandler
        {
            TickerExceptionHandlerType = typeof(THandler);
            return this;
        }

        internal Models.FailureWebhookOptions FailureWebhook { get; set; }

        /// <summary>
        /// POST a JSON event to <paramref name="url"/> whenever a job fails
        /// terminally (retries exhausted), exceeds its execution timeout, or the
        /// stale watchdog recovers jobs from a dead node. Delivery is async and
        /// buffered — it never blocks execution — with a couple of send retries.
        /// For Slack/email/custom sinks, register your own
        /// <see cref="Interfaces.ITickerQFailureNotifier"/> instead.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> NotifyFailuresViaWebhook(
            string url, System.Collections.Generic.IDictionary<string, string> headers = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Webhook URL must be provided.", nameof(url));

            FailureWebhook = new Models.FailureWebhookOptions { Url = url, Headers = headers };
            return this;
        }
        
        internal void UseExternalProviderApplication(Action<IServiceProvider> action)
            => _tickerExecutionContext.ExternalProviderApplicationAction = action;
        
        internal void UseDashboardApplication(Action<object> action)
            => _tickerExecutionContext.DashboardApplicationAction = action;
    }

    public class SchedulerOptionsBuilder
    {
        public string NodeIdentifier { get; set; } = Environment.MachineName;
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
        public TimeSpan IdleWorkerTimeOut { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>
        /// Gets or sets the minimum interval between database polls.
        /// Prevents tight loops when tasks are due or when the database is empty.
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
        /// On graceful shutdown, how long to wait for in-flight ticker executions
        /// to finish before letting the host exit. Zero disables draining (jobs are
        /// abandoned and later healed by stale-job recovery). Bounded additionally
        /// by the host's own shutdown timeout (<c>HostOptions.ShutdownTimeout</c>).
        /// </summary>
        public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
